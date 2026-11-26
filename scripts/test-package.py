import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading

entry = Path(sys.argv[1]).resolve()
command = ['dotnet', str(entry)] if entry.suffix == '.dll' else [str(entry)]
missing_extension = '--missing-extension' in sys.argv[2:]
with tempfile.TemporaryDirectory(prefix='dbmcp-packaged-smoke-') as temporary:
    work = Path(temporary)
    config = work / 'databases.json'
    config.write_text(json.dumps({'databases': [{
        'name': 'smoke', 'dbType': 'Sqlite', 'isDefault': True,
        'connectionString': f'Data Source={work / "probe.db"};Pooling=False',
        'enableDangerousOperations': False
    }, {
        'name': 'sqlserver', 'dbType': 'SqlServer', 'connectionString': 'Server=localhost,1;Database=test;User ID=test;Password=unused;TrustServerCertificate=true'
    }, {
        'name': 'mongo', 'dbType': 'MongoDb', 'connectionString': 'mongodb://localhost:1/test'
    }, {
        'name': 'highgo', 'dbType': 'HG', 'connectionString': 'Server=localhost;Port=1;Database=test'
    }, {
        'name': 'gbase', 'dbType': 'GBase', 'connectionString': 'Host=localhost;Service=1;Server=gbase01;Database=test;Protocol=onsoctcp;Uid=test;Pwd=test'
    }, {
        'name': 'tdengine', 'dbType': 'TDengine', 'connectionString': 'Host=localhost;Port=1;Username=root;Password=test;Database=test'
    }, {
        'name': 'duck', 'dbType': 'DuckDB', 'connectionString': f'DataSource={work / "probe.duckdb"}',
        'enableDangerousOperations': True
    }]}), encoding='utf-8')
    environment = os.environ.copy()
    environment.update(DB_CONFIG_PATH=str(config), ENABLE_MONITOR_CONFIG='false', SEQ_SERVER_URL='', SEQ_API_KEY='', DB_MCP_TOOL_PROFILE='full')
    version = subprocess.run(command + ['--version'], cwd=work, env=environment, capture_output=True, text=True, check=True).stdout.strip()
    cli_catalog = json.loads(subprocess.run(command + ['tool', 'list', '--json'], cwd=work, env=environment,
        capture_output=True, text=True, check=True).stdout)['tools']
    cli = subprocess.run(command + ['tool', 'query_database', '--sql', 'PRAGMA user_version=2026', '--config', str(config)],
                         cwd=work, env=environment, capture_output=True, text=True, encoding='utf-8', timeout=30)
    assert cli.returncode != 0, cli.stdout
    assert json.loads(cli.stdout)['errorCode'] == 1003, cli.stdout

    process = subprocess.Popen(command, cwd=work, env=environment, stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
    messages = queue.Queue()

    def read_output():
        for line in process.stdout:
            try:
                messages.put((json.loads(line), len(line.encode('utf-8'))))
            except json.JSONDecodeError:
                messages.put(({'unexpected_stdout': line[:100]}, 0))

    def drain_errors():
        for _ in process.stderr:
            pass

    threading.Thread(target=read_output, daemon=True).start()
    threading.Thread(target=drain_errors, daemon=True).start()
    request_id = 0
    last_wire_bytes = 0

    def send(method, parameters, notification=False):
        global request_id, last_wire_bytes
        request_id += 1
        request = {'jsonrpc': '2.0', 'method': method, 'params': parameters}
        if not notification:
            request['id'] = request_id
        process.stdin.write(json.dumps(request) + '\n')
        process.stdin.flush()
        if notification:
            return
        while True:
            message, wire_bytes = messages.get(timeout=30)
            assert 'unexpected_stdout' not in message, message
            if message.get('id') == request_id:
                assert 'error' not in message, message
                last_wire_bytes = wire_bytes
                return message['result']

    def query(sql, parameters=None):
        arguments = {'sql': sql}
        if parameters is not None:
            arguments['parameters'] = parameters
        result = send('tools/call', {'name': 'query_database', 'arguments': arguments})
        return result, json.loads(result['content'][0]['text'])

    try:
        initialized = send('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                         'clientInfo': {'name': 'package-validation', 'version': '1'}})
        assert initialized['protocolVersion'] == '2025-11-25'
        assert initialized['serverInfo']['version'] == version
        send('notifications/initialized', {}, True)
        tools = send('tools/list', {})['tools']
        expected = json.loads((Path(__file__).resolve().parents[1] / '.mcp/server.json').read_text(encoding='utf-8'))['tools']
        tool_names = {t['name'] for t in tools}
        assert tool_names and len(tools) == len(expected) == len(cli_catalog) == len(tool_names)
        assert tool_names == {t['name'] for t in expected} == {t['name'] for t in cli_catalog}
        for tool in tools:
            descriptor = next(t for t in cli_catalog if t['name'] == tool['name'])
            assert descriptor['inputSchema'] == tool['inputSchema'], tool['name']
            assert descriptor.get('outputSchema') == tool.get('outputSchema'), tool['name']
        assert not {'sql_query', 'sql_query_with_in_parameter', 'call_stored_procedure_with_output'} & tool_names
        assert {'query_database', 'query_table_page', 'get_database_capabilities'}.issubset(tool_names)
        schema = next(t for t in tools if t['name'] == 'query_database')['outputSchema']
        assert schema['type'] == 'object' and 'configurationVersion' in schema['properties']
        typed = send('tools/call', {'name': 'query_database', 'arguments': {
            'sql': 'select @v as value', 'databaseName': 'smoke',
            'parameters': [{'name': 'v', 'value': '9007199254740993', 'dbType': 'Int64'}]}})
        assert not typed.get('isError', False)
        structured = typed['structuredContent']
        assert structured == json.loads(typed['content'][0]['text'])
        assert structured['databaseName'] == 'smoke' and structured['data'][0]['value'] == 9007199254740993
        tool_bytes = len(typed['content'][0]['text'].encode('utf-8'))
        envelope_bytes = last_wire_bytes
        capabilities = send('tools/call', {'name': 'get_database_capabilities', 'arguments': {'databaseName': 'smoke'}})
        assert json.loads(capabilities['content'][0]['text'])['capabilities']['boundedMultipleResults'] == 'supported'
        for name, provider in [('highgo', 'HG'), ('gbase', 'GBase'), ('tdengine', 'TDengine'), ('duck', 'DuckDB')]:
            response = send('tools/call', {'name': 'get_database_capabilities', 'arguments': {'databaseName': name}})
            payload = json.loads(response['content'][0]['text'])
            if missing_extension and name == 'highgo':
                assert response['isError'] and payload['errorCode'] == 1012, response
                assert 'SqlSugar.HGCore.dll' in payload['errorMessage'], payload
            else:
                assert not response.get('isError', False), response
                assert payload['actualProvider'] == provider, payload
        for sql in ['create table driver_probe(id bigint, label varchar)',
                    "insert into driver_probe values (9007199254740993, '驱动')"]:
            response = send('tools/call', {'name': 'execute_command', 'arguments': {'databaseName': 'duck', 'sql': sql}})
            assert not response.get('isError', False), response
        duck = send('tools/call', {'name': 'query_database', 'arguments': {
            'databaseName': 'duck', 'sql': 'select id, label from driver_probe where id = @id',
            'parameters': [{'name': 'id', 'value': '9007199254740993', 'dbType': 'Int64'}]}})
        assert not duck.get('isError', False), duck
        assert duck['structuredContent']['data'] == [{'id': 9007199254740993, 'label': '驱动'}], duck
        mongo = send('tools/call', {'name': 'get_database_capabilities', 'arguments': {'databaseName': 'mongo'}})
        assert not mongo.get('isError', False), mongo
        assert json.loads(mongo['content'][0]['text'])['capabilities']['sqlTools'] == 'unsupported'
        assert json.loads(mongo['content'][0]['text'])['capabilities']['collectionQueries'] == 'supported-find-aggregate'
        mongo_find = next(t for t in tools if t['name'] == 'mongo_find')
        assert 'object' in mongo_find['inputSchema']['properties']['filter']['type']
        for invalid in [None, [], '{}', {'$where': 'private-test-value'}]:
            response = send('tools/call', {'name': 'mongo_find', 'arguments': {
                'databaseName': 'mongo', 'collection': 'docs', 'filter': invalid}})
            assert response['isError'] and response['structuredContent']['errorCode'] == 1004, response
            assert response['structuredContent'] == json.loads(response['content'][0]['text'])
        for name in ['mongo_insert_many', 'mongo_update', 'mongo_delete', 'mongo_truncate']:
            assert next(t for t in tools if t['name'] == name)['annotations']['destructiveHint']
        mongo_query = send('tools/call', {'name': 'query_database', 'arguments': {'databaseName': 'mongo', 'sql': 'select 1'}})
        assert mongo_query['isError'] and json.loads(mongo_query['content'][0]['text'])['errorCode'] == 1012
        result, payload = query('PRAGMA user_version=2026')
        assert result['isError'] and payload['errorCode'] == 1003
        result, payload = query('PRAGMA user_version')
        assert not result.get('isError', False) and payload['data'][0]['user_version'] == 0
        result, payload = query('select CAST(@value AS REAL) as tiny', [{'name': 'value', 'value': 1e-50}])
        assert payload['data'][0]['tiny'] == 1e-50, payload
        result, payload = query('select @value as value', [{'name': 'value', 'dbType': 'Double', 'value': '1e400'}])
        assert result['isError'] and payload['errorCode'] == 1004, payload
        result, payload = query('select * from missing_smoke_table')
        assert result['isError'] and not payload['success']
        print(json.dumps({'entry': str(entry), 'cliFailureExit': cli.returncode, 'toolCount': len(tools),
                          'protocol': initialized['protocolVersion'], 'readOnlyStateUnchanged': True,
                          'tinyNumberPreserved': True, 'invalidNumberRejected': True, 'mcpErrorsMarked': True,
                          'structuredContentMatchesText': True, 'duckDbNativeReadWrite': True,
                          'missingExtensionIsolated': missing_extension,
                          'toolJsonBytes': tool_bytes, 'stdioLineUtf8Bytes': envelope_bytes}))
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
