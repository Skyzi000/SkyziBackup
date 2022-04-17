using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace SkyziBackup.Data
{
    public abstract class SaveableData : IDisposable
    {
        [JsonIgnore]
        public virtual string? SaveFileName { get; }

        [JsonIgnore]
        public Timer SaveTimer
        {
            get
            {
                ThrowIfDisposed();
                return _saveTimer ??= new Timer();
            }
            set => _saveTimer = value;
        }

        private Timer? _saveTimer;

        [JsonIgnore]
        public SemaphoreSlim Semaphore
        {
            get
            {
                ThrowIfDisposed();
                return _semaphore ??= new SemaphoreSlim(1, 1);
            }
            set => _semaphore = value;
        }

        [JsonIgnore]
        public bool IsDisposed { get; private set; }

        private SemaphoreSlim? _semaphore;


        public virtual void StartAutoSave(double intervalMsec)
        {
            ThrowIfDisposed();
            _saveTimer?.Stop();
            _saveTimer?.Dispose();
            _saveTimer = null;
            SaveTimer.Interval = intervalMsec;
            SaveTimer.Elapsed += (s, e) =>
            {
                if (Semaphore.CurrentCount != 0)
                    AutoSave();
            };
            SaveTimer.Start();
        }

        public virtual void AutoSave() => Save();

        public virtual void Save(string? filePath = null)
        {
            ThrowIfDisposed();
            Semaphore.Wait();
            try
            {
                DataFileWriter.Write(this, filePath, true);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual async Task SaveAsync(string? filePath = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await Semaphore.WaitAsync(CancellationToken.None);
            try
            {
                await DataFileWriter.WriteAsync(this, filePath, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual void Delete()
        {
            ThrowIfDisposed();
            Semaphore.Wait();
            try
            {
                DataFileWriter.Delete(this);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    _semaphore?.Dispose();
                    _semaphore = null;
                    _saveTimer?.Stop();
                    _saveTimer?.Dispose();
                    _saveTimer = null;
                }

                IsDisposed = true;
            }
        }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
