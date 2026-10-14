namespace Exportaramus.Features.Snapshots;

public sealed class AtomicFileOperations : IAtomicFileOperations
{
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
    public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
    public void Move(string source, string destination) => File.Move(source, destination);
    public void Delete(string path) => File.Delete(path);
}
