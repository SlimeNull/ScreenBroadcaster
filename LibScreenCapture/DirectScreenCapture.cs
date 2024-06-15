using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Win32.Graphics.Gdi;

namespace LibScreenCapture
{
    public class DirectScreenCapture : IScreenCapture, IDisposable
    {
        private readonly Factory1 _factory;
        private readonly Adapter1 _adapter;
        private readonly SharpDX.Direct3D11.Device _device;

        private readonly Output _output;
        private readonly Output1 _output1;

        private readonly Texture2D _screenTexture;
        private readonly nint _dataPointer;
        private readonly OutputDuplication _duplicatedOutput;
        private readonly nuint _dataByteCount;
        private readonly int _pixelBytes;
        private readonly int _stride;
        private bool _disposedValue;

        public nint DataPointer => _dataPointer;
        public int PixelBytes => _pixelBytes;
        public int Stride => _stride;

        public int Width => _output.Description.DesktopBounds.Right;
        public int Height => _output.Description.DesktopBounds.Bottom;


        public unsafe DirectScreenCapture(int adapterIndex)
        {
            _factory = new Factory1();
            _adapter = _factory.GetAdapter1(adapterIndex);
            _device = new(_adapter);

            _output = _adapter.GetOutput(0);
            _output1 = _output.QueryInterface<Output1>();

            _screenTexture = new Texture2D(_device,
                new Texture2DDescription()
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = _output.Description.DesktopBounds.Right,
                    Height = _output.Description.DesktopBounds.Bottom,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription =
                    {
                        Count = 1,
                        Quality = 0
                    },
                    Usage = ResourceUsage.Staging
                });

            _pixelBytes = 4;
            _stride = _output.Description.DesktopBounds.Right * _pixelBytes;
            _dataByteCount = (nuint)(_stride * _output.Description.DesktopBounds.Bottom);
            _dataPointer = (nint)NativeMemory.Alloc(_dataByteCount);
            _duplicatedOutput = _output1.DuplicateOutput(_device);
        }

        public bool Capture() => Capture(default);

        public unsafe bool Capture(TimeSpan timeout)
        {
            var result = _duplicatedOutput.TryAcquireNextFrame((int)timeout.TotalMilliseconds, out var frameInfo, out var screenResource);

            if (!result.Success)
                return false;

            using Texture2D capturedScreenTexture = screenResource.QueryInterface<Texture2D>();

            // copy data
            _device.ImmediateContext.CopyResource(capturedScreenTexture, _screenTexture);
            var mapSource = _device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

            // copy data to memory
            var textureDataPointer = mapSource.DataPointer.ToPointer();
            NativeMemory.Copy(textureDataPointer, (void*)_dataPointer, _dataByteCount);

            // release resources
            _device.ImmediateContext.UnmapSubresource(_screenTexture, 0);

            screenResource.Dispose();
            _duplicatedOutput.ReleaseFrame();

            return true;
        }

        protected virtual unsafe void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null

                _factory.Dispose();
                _adapter.Dispose();
                _device.Dispose();
                _output1.Dispose();
                _output.Dispose();
                _screenTexture.Dispose();
                _duplicatedOutput.Dispose();

                NativeMemory.Free((void*)_dataPointer);

                _disposedValue = true;
            }
        }

        // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~DirectScreenCapture()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
