namespace DatabaseMcpServer.Helpers;

internal sealed class BudgetedJsonStream(long maximum) : Stream
{
    private long _maximum = maximum;
    private readonly MemoryStream _buffer = new();

    public void Reset(long limit)
    {
        _maximum = limit;
        _buffer.SetLength(0);
        _buffer.Position = 0;
    }

    public byte[] ToArray() => _buffer.ToArray();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _buffer.Length;
    public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Length + buffer.Length > _maximum) throw new ResponseBudgetExceededException();
        _buffer.Write(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    protected override void Dispose(bool disposing)
    {
        if (disposing) _buffer.Dispose();
        base.Dispose(disposing);
    }
}
