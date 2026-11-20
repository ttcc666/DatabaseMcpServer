using System.Diagnostics;
using System.Security.AccessControl;
using System.Text.Json;

namespace DatabaseMcpServer.Helpers;

internal sealed class AtomicJsonFileWrite : IDisposable
{
    private readonly FileStream _lockFile;
    private readonly string _path;

    public AtomicJsonFileWrite(string path)
    {
        var file = new FileInfo(path);
        _path = file.LinkTarget == null ? file.FullName : file.ResolveLinkTarget(true)!.FullName;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                _lockFile = File.Open(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33 or 35)
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    throw new IOException($"配置文件正由其他操作写入，请重试: {_path}", ex);
                }

                Thread.Sleep(25);
            }
        }
    }

    public bool Exists => File.Exists(_path);

    public void Save<T>(T document)
    {
        ObjectDisposedException.ThrowIf(!_lockFile.CanWrite, this);
        var destination = new FileInfo(_path);
        var exists = destination.Exists;
        var temporary = new FileInfo(Path.Combine(destination.DirectoryName!, $".dbmcp-{Guid.NewGuid():N}.tmp"));
        var stream = CreateTemporaryFile(temporary, exists ? destination : null);
        try
        {
            using (stream)
            {
                if (exists && !OperatingSystem.IsWindows())
                {
                    PrepareUnixReplacement(destination.FullName, temporary.FullName);
                    stream.SetLength(0);
                }

                JsonSerializer.Serialize(stream, document, JsonSerializationDefaults.IndentedCamelCase);
                stream.Flush(flushToDisk: true);
            }

            if (exists)
            {
                File.Replace(temporary.FullName, _path, null);
            }
            else
            {
                File.Move(temporary.FullName, _path);
            }
        }
        finally
        {
            temporary.Delete();
        }
    }

    private static void PrepareUnixReplacement(string source, string temporary)
    {
        using var access = File.OpenHandle(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var start = new ProcessStartInfo("/bin/cp") { UseShellExecute = false, RedirectStandardError = true };
        if (OperatingSystem.IsLinux())
        {
            start.ArgumentList.Add("--attributes-only");
            start.ArgumentList.Add("--preserve=mode,ownership");
        }
        else
        {
            start.ArgumentList.Add("-p");
        }

        start.ArgumentList.Add(source);
        start.ArgumentList.Add(temporary);
        using var copy = Process.Start(start)!;
        var error = copy.StandardError.ReadToEnd();
        copy.WaitForExit();
        if (copy.ExitCode != 0)
        {
            throw new IOException($"无法保留原配置文件的访问权限: {error.Trim()}");
        }
    }

    private static FileStream CreateTemporaryFile(FileInfo file, FileInfo? destination)
    {
        if (OperatingSystem.IsWindows() && destination != null)
        {
            var permissions = destination.GetAccessControl(AccessControlSections.Access);
            permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            return file.Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, permissions);
        }

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(file.FullName, options);
    }

    public void Dispose() => _lockFile.Dispose();
}
