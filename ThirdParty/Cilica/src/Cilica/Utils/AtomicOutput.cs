namespace Cilica.Util;

/// <summary> Stages output files and restores their input versions if publication fails. </summary>
internal sealed class AtomicOutput : IDisposable
{
    readonly List<OutputFile> _files = new();
    bool _committed, _disposed;

    sealed class OutputFile(string path, string? backup)
    {
        public string Path = path;
        public string? Backup = backup;
        public string Temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        public string Recovery = path + "." + Guid.NewGuid().ToString("N") + ".recovery";
        public FileStream Stream = null!;
        public bool Installed, HadOriginal, PreserveRecovery;
    }

    public Stream Create(string path, string? backup = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Ensure.That(!_committed, "Output has already been published");

        path = System.IO.Path.GetFullPath(path);
        backup = backup == null ? null : System.IO.Path.GetFullPath(backup);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Ensure.That(!string.Equals(path, backup, comparison), "Backup must differ from output");
        Ensure.That(!_files.Any(file => string.Equals(file.Path, path, comparison)), "Output paths must be distinct");
        Ensure.That(!_files.Any(file => string.Equals(file.Backup, path, comparison) ||
            backup != null && (string.Equals(file.Path, backup, comparison) || string.Equals(file.Backup, backup, comparison))),
            "Output and backup paths must not overlap");

        var file = new OutputFile(path, backup);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        file.Stream = new FileStream(file.Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        _files.Add(file);

        return file.Stream;
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Ensure.That(!_committed, "Output has already been published");

        foreach (var file in _files) {
            file.Stream.Dispose();
        }

        try {
            foreach (var file in _files) {
                file.HadOriginal = File.Exists(file.Path);

                if (file.HadOriginal) {
                    File.Replace(file.Temporary, file.Path, file.Recovery);
                } else {
                    File.Move(file.Temporary, file.Path);
                }

                file.Installed = true;
            }

            foreach (var file in _files) {
                if (file.HadOriginal && file.Backup != null) {
                    File.Copy(file.Recovery, file.Backup, overwrite: true);
                }
            }

            _committed = true;
        } catch (Exception publicationError) {
            var errors = new List<Exception>() { publicationError };

            foreach (var file in _files.AsEnumerable().Reverse()) {
                if (!file.Installed) {
                    continue;
                }

                try {
                    if (file.HadOriginal) {
                        File.Move(file.Recovery, file.Path, overwrite: true);
                    } else {
                        File.Delete(file.Path);
                    }
                } catch (Exception recoveryError) {
                    file.PreserveRecovery = true;
                    errors.Add(new IOException($"Could not restore '{file.Path}'; recovery image: '{file.Recovery}'", recoveryError));
                }
            }

            if (errors.Count > 1) {
                throw new AggregateException(errors);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _disposed = true;

        foreach (var file in _files) {
            file.Stream.Dispose();
            File.Delete(file.Temporary);

            if (!file.PreserveRecovery) {
                File.Delete(file.Recovery);
            }
        }
    }
}
