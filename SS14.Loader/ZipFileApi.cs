using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Robust.LoaderApi;

namespace SS14.Loader;

/// <summary>
/// Serves files to the engine out of a zip archive.
/// </summary>
/// <remarks>
/// <see cref="ZipArchive"/> is not thread-safe, but the engine reads resources from many threads
/// in parallel. Instead of serializing every read behind a single lock, we keep a pool of
/// archives - each backed by its own read-only handle to the same file - so reads run concurrently.
/// This mirrors the connection pool in <see cref="ContentDbFileApi"/>.
/// </remarks>
internal sealed class ZipFileApi : IFileApi, IDisposable
{
    private readonly string _zipPath;
    private readonly string? _prefix;
    private readonly int _poolSize;
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly ConcurrentBag<ZipArchive> _pool = new();

    public ZipFileApi(string zipPath, string? prefix)
    {
        _zipPath = zipPath;
        _prefix = prefix;

        _poolSize = Math.Max(2, Environment.ProcessorCount);
        _poolSemaphore = new SemaphoreSlim(_poolSize, _poolSize);

        for (var i = 0; i < _poolSize; i++)
            _pool.Add(OpenArchive());
    }

    private ZipArchive OpenArchive()
    {
        // FileShare.Read (the File.OpenRead default) lets every pooled archive
        // hold its own independent handle to the same file.
        return new ZipArchive(File.OpenRead(_zipPath), ZipArchiveMode.Read);
    }

    public bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream)
    {
        var fullPath = _prefix != null ? _prefix + path : path;

        _poolSemaphore.Wait();
        ZipArchive? archive = null;
        try
        {
            if (!_pool.TryTake(out archive))
                throw new InvalidOperationException("Entered semaphore but failed to retrieve a zip archive?");

            var entry = archive.GetEntry(fullPath);
            if (entry == null)
            {
                stream = null;
                return false;
            }

            // Pre-size the buffer to the uncompressed length so the
            // MemoryStream never reallocates while we copy into it.
            var ms = new MemoryStream((int)entry.Length);
            using (var zipStream = entry.Open())
                zipStream.CopyTo(ms);

            ms.Position = 0;
            stream = ms;
            return true;
        }
        finally
        {
            if (archive != null)
                _pool.Add(archive);

            _poolSemaphore.Release();
        }
    }

    public IEnumerable<string> AllFiles
    {
        get
        {
            _poolSemaphore.Wait();
            ZipArchive? archive = null;
            try
            {
                if (!_pool.TryTake(out archive))
                    throw new InvalidOperationException("Entered semaphore but failed to retrieve a zip archive?");

                IEnumerable<ZipArchiveEntry> entries = archive.Entries.Where(e => e.Name != "");
                if (_prefix != null)
                {
                    return entries
                        .Where(e => e.FullName.StartsWith(_prefix))
                        .Select(e => e.FullName[_prefix.Length..])
                        .ToList();
                }

                return entries.Select(e => e.FullName).ToList();
            }
            finally
            {
                if (archive != null)
                    _pool.Add(archive);

                _poolSemaphore.Release();
            }
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < _poolSize; i++)
        {
            _poolSemaphore.Wait();
            if (_pool.TryTake(out var archive))
                archive.Dispose();
        }

        _poolSemaphore.Dispose();
    }
}
