namespace Exportaramus.Features.Snapshots;

public interface IAtomicFileOperations
{
    Stream OpenRead(string path);
    FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
        new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
    void Replace(string source, string destination, string? backup);
    void Move(string source, string destination);
    void Delete(string path);
    void Copy(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
}
