using Microsoft.Extensions.Options;

namespace NavEstate.Api.Common;

public class FileStoreOptions
{
    public const string Section = "Files";
    /// <summary>e.g. D:\NavEstate\files on the server. Files are only served through the API.</summary>
    public string Root { get; set; } = "App_Data/files";
    public long MaxBytes { get; set; } = 10_000_000;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png", ".webp", ".docx", ".xlsx"];
}

public interface IFileStore
{
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default);
    Stream? Open(string relativePath);
    void Delete(string relativePath);
}

public class LocalFileStore(IOptions<FileStoreOptions> options, IWebHostEnvironment env) : IFileStore
{
    string Root => Path.GetFullPath(Path.IsPathRooted(options.Value.Root) ? options.Value.Root : Path.Combine(env.ContentRootPath, options.Value.Root));

    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var safeFolder = string.Join('/', folder.Split('/', '\\').Where(p => p.Length > 0 && p != "." && p != ".."));
        var rel = $"{safeFolder}/{Guid.NewGuid():N}{ext}";
        var full = Resolve(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var f = File.Create(full);
        await content.CopyToAsync(f, ct);
        return rel;
    }

    public Stream? Open(string relativePath)
    {
        var full = Resolve(relativePath);
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    public void Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (File.Exists(full)) File.Delete(full);
    }

    string Resolve(string rel)
    {
        var full = Path.GetFullPath(Path.Combine(Root, rel));
        if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid path");
        return full;
    }
}
