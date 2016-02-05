﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;

using Device = SharpDX.Direct3D11.Device;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Direct3DRendering
{
    public abstract class Renderer : IDisposable
    {
        protected bool _initializeRendererIsRunning;
        public bool IsBusy
        {
            get { return _initializeRendererIsRunning; }
        }

        protected RenderType _renderType = RenderType.NotSpecified;

        protected FeatureLevel _featureLevel;
        protected RenderWindow _dataContextRenderWindow;
        protected RenderControl _parent;
        protected Device _device;

        protected SwapChain _swapChain;

        protected SharpDX.Toolkit.Graphics.GraphicsDevice _graphicsDevice;

        protected OrbitCamera _orbitCamera;

        protected Texture2D _backBuffer;
        protected Texture2D _depthBuffer;
        protected RenderTargetView _renderView;
        protected DepthStencilView _depthView;

        protected Axes _axes;
        protected BoundingBox _boundingBox;
        protected CoordinateBox _coordinateBox;

        protected bool _needsResize;
        protected bool _dataLoaded;
        protected const float _clipDistance = 1.0f;

        protected Plane _nearClippingPlane;
        protected Plane _farClippingPlane;

        protected bool ShowAxes
        {
            get
            {
                if (_dataContextRenderWindow != null)
                {
                    return _dataContextRenderWindow.RenderWindowView.ShowAxes;
                }
                return false;
            }
        }
        protected bool ShowBoundingBox
        {
            get
            {
                if (_dataContextRenderWindow != null)
                {
                    return _dataContextRenderWindow.RenderWindowView.ShowBoundingBox;
                }
                return false;
            }
        }
        protected bool ShowCoordinateBox
        {
            get
            {
                if (_dataContextRenderWindow != null)
                {
                    return _dataContextRenderWindow.RenderWindowView.ShowCoordinateBox;
                }
                return false;
            }
        }
        protected float CoordinateBoxTransparency
        {
            get
            {
                if (_dataContextRenderWindow != null)
                {
                    return _dataContextRenderWindow.RenderWindowView.CoordinateBoxTransparency;
                }
                return 0;
            }
        }
        protected Color BackColor
        {
            get
            {
                if (_dataContextRenderWindow != null)
                {
                    return _dataContextRenderWindow.RenderWindowView.BackColor;
                }
                return Color.Black;
            }
        }
        protected float Brightness
        {
            get
            {
                if (_dataContextRenderWindow != null)
                {
                    return _dataContextRenderWindow.RenderWindowView.Brightness;
                }
                return 1.0f;
            }
        }

        public RenderType RenderType
        {
            get { return _renderType; }
        }

        public bool NeedsResize
        {
            get { return _needsResize; }
            set { _needsResize = value; }
        }
        public OrbitCamera Camera
        {
            get { return _orbitCamera; }
        }

        public bool DataLoaded
        {
            get { return _dataLoaded; }
        }

        public Device Device
        {
            get { return _device; }
        }
        public Texture2D BackBuffer
        {
            get { return _backBuffer; }
        }
        public SharpDX.Toolkit.Graphics.Texture2D BackBufferTK
        {
            get
            {
                return SharpDX.Toolkit.Graphics.Texture2D.New(_graphicsDevice, BackBuffer);
            }
        }
        public byte[] GetCurrentCapture(out int Width, out int Height)
        {
            Width = 0;
            Height = 0;

            if (NeedsResize) return null;

            SharpDX.Toolkit.Graphics.Texture2D tex = SharpDX.Toolkit.Graphics.Texture2D.New(_graphicsDevice,
                Texture2D.FromSwapChain<Texture2D>(_swapChain, 0));
            byte[] buffer = new byte[tex.Width * tex.Height * 4];
            tex.GetData(buffer);

            Width = tex.Width;
            Height = tex.Height;

            tex.Dispose();

            return buffer;
        }

        public Renderer(RenderWindow Window)
        {
            _dataContextRenderWindow = Window;
            _parent = Window.RenderControl;
        }

        public virtual void InitializeRenderer()
        {
            // Thread safe method to access window handle. This allows the function calling this
            // to be run asynchronously.
            IntPtr parentHandle = IntPtr.Zero;
            IntPtr windowHandle = IntPtr.Zero;
            _parent.Invoke(new System.Windows.Forms.MethodInvoker(delegate { parentHandle = _parent.Handle; }));
            _dataContextRenderWindow.Dispatcher.Invoke(
                () => windowHandle = new System.Windows.Interop.WindowInteropHelper(_dataContextRenderWindow).Handle);

            var desc = new SwapChainDescription()
            {
                BufferCount = 1,
                ModeDescription = new ModeDescription(_parent.ClientSize.Width, _parent.ClientSize.Height,
                    new Rational(60, 1), Format.R8G8B8A8_UNorm),
                IsWindowed = true,
                OutputHandle = parentHandle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput,
            };

            DeviceCreationFlags dcFlags = DeviceCreationFlags.None;

#if DEBUG_DEVICE
            dcFlags = DeviceCreationFlags.Debug;
#endif
            Device.CreateWithSwapChain(DriverType.Hardware, dcFlags,
                desc, out _device, out _swapChain);

            _graphicsDevice = SharpDX.Toolkit.Graphics.GraphicsDevice.New(_device);

            var context = _device.ImmediateContext;
            var factory = _swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(parentHandle, WindowAssociationFlags.IgnoreAll);

            _featureLevel = _device.FeatureLevel;

            _orbitCamera = new OrbitCamera(_device, _parent, windowHandle);

            // Set camera position so that (-x, -y, -z) of the volume is the top of the rendering
            _orbitCamera.SetInitialConditions(new Vector3(0, -5f, -2.5f), new Vector3(0, 1, 0));

            _needsResize = true;

            _axes = new Axes(_device);

            InitializeShaders();
            InitializeStates();
        }
        protected abstract void InitializeShaders();
        protected virtual void InitializeStates()
        {
            BlendStateDescription descBlend = new BlendStateDescription();
            descBlend = BlendStateDescription.Default();
            descBlend.RenderTarget[0].IsBlendEnabled = true;
            descBlend.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
            descBlend.RenderTarget[0].DestinationBlend = BlendOption.InverseSourceAlpha;
            descBlend.RenderTarget[0].BlendOperation = BlendOperation.Add;
            descBlend.RenderTarget[0].SourceAlphaBlend = BlendOption.Zero;
            descBlend.RenderTarget[0].DestinationAlphaBlend = BlendOption.Zero;
            descBlend.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;
            descBlend.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;

            BlendState state = new BlendState(_device, descBlend);
            _device.ImmediateContext.OutputMerger.SetBlendState(state);
        }

        protected virtual bool Resize()
        {
            if (_device == null || _swapChain == null)
            {
                return false;
            }

            var context = _device.ImmediateContext;

            Disposer.RemoveAndDispose(ref _backBuffer);
            Disposer.RemoveAndDispose(ref _renderView);
            Disposer.RemoveAndDispose(ref _depthBuffer);
            Disposer.RemoveAndDispose(ref _depthView);

            SwapChainDescription desc = _swapChain.Description;
            _swapChain.ResizeBuffers(desc.BufferCount, _parent.ClientSize.Width,
                _parent.ClientSize.Height, Format.Unknown, SwapChainFlags.None);

            _backBuffer = Texture2D.FromSwapChain<Texture2D>(_swapChain, 0);
            _renderView = new RenderTargetView(_device, _backBuffer);

            _depthBuffer = new Texture2D(_device, new Texture2DDescription()
            {
                Format = Format.D32_Float_S8X24_UInt,
                ArraySize = 1,
                MipLevels = 1,
                Width = _parent.ClientSize.Width,
                Height = _parent.ClientSize.Height,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            });
            _depthView = new DepthStencilView(_device, _depthBuffer);

            context.Rasterizer.SetViewport(new Viewport(0, 0, _parent.ClientSize.Width, _parent.ClientSize.Height));
            context.OutputMerger.SetTargets(_depthView, _renderView);

            return true;
        }

        public virtual void Update(bool targetYAxisOrbiting)
        {
            if (_needsResize)
            {
                if (Resize()) _needsResize = false;
            }

             _orbitCamera.UpdateCamera(targetYAxisOrbiting);

             Matrix viewProj = _orbitCamera.WorldProjView;

             _nearClippingPlane = new Plane(
                 viewProj.M14 + viewProj.M13,
                 viewProj.M23 + viewProj.M23,
                 viewProj.M34 + viewProj.M33,
                 viewProj.M44 + viewProj.M43);
             _nearClippingPlane.Normalize();

             _farClippingPlane = new Plane(
                 viewProj.M14 - viewProj.M13,
                 viewProj.M24 - viewProj.M23,
                 viewProj.M34 - viewProj.M33,
                 viewProj.M44 - viewProj.M43);
             _farClippingPlane.Normalize();            
        }
        public virtual void Draw()
        {
            if (_device == null) return;

            var context = _device.ImmediateContext;

            context.OutputMerger.SetRenderTargets(_depthView, _renderView);
            context.ClearDepthStencilView(_depthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, _clipDistance, 0);
            context.ClearRenderTargetView(_renderView, BackColor);

            Matrix worldProjView = _orbitCamera.WorldProjView;
            worldProjView.Transpose();

            if (_axes != null && ShowAxes)
            {
                _axes.Update(worldProjView);
                _axes.Draw();
            }
            if (_boundingBox != null && ShowBoundingBox)
            {
                _boundingBox.Update(worldProjView);
                _boundingBox.Draw();
            }
        }

        protected void CompleteDraw()
        {
            if (_coordinateBox != null && ShowCoordinateBox)
            {
                Matrix worldProjView = _orbitCamera.WorldProjView;
                worldProjView.Transpose();

                _coordinateBox.Update(worldProjView, CoordinateBoxTransparency);
                _coordinateBox.Draw();
            }

            _swapChain.Present(0, PresentFlags.None);
        }

        #region IDisposable
        ~Renderer()
        {
            Dispose(false);
        }
        private bool _disposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool Disposing)
        {
            if (!_disposed)
            {
                if (Disposing)
                {

                }
                Disposer.RemoveAndDispose(ref _orbitCamera);

                Disposer.RemoveAndDispose(ref _backBuffer);
                Disposer.RemoveAndDispose(ref _renderView);
                Disposer.RemoveAndDispose(ref _depthBuffer);
                Disposer.RemoveAndDispose(ref _depthView);

                Disposer.RemoveAndDispose(ref _device);
                Disposer.RemoveAndDispose(ref _graphicsDevice);
                Disposer.RemoveAndDispose(ref _swapChain);

                _disposed = true;
            }
        }
        #endregion
    }
}