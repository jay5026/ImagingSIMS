﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImagingSIMS.Direct3DRendering.Controls;
using ImagingSIMS.Direct3DRendering.DrawingObjects;
using ImagingSIMS.Direct3DRendering.SceneObjects;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using BoundingBox = ImagingSIMS.Direct3DRendering.SceneObjects.BoundingBox;

namespace ImagingSIMS.Direct3DRendering.Renderers
{
    public class VolumeRenderer : Renderer
    {
        VertexShader _vsPosition;
        VertexShader _vsRaycast;
        InputLayout _ilPosition;
        InputLayout _ilRaycast;
        PixelShader _psPosition;
        PixelShader _psRaycast;
        GeometryShader _gsPosition;
        GeometryShader _gsRaycast;

        Texture2D[] _texPositions;
        ShaderResourceView[] _srvPositions;
        RenderTargetView[] _rtvPositions;
        SamplerState _samplerLinear;

        Texture3D[] _texVolumes;
        ShaderResourceView[] _srvVolumes;

        Texture3D _texActiveVoxels;
        ShaderResourceView _srvActiveVoxels;

        Buffer _vertexBufferCube;
        Buffer _indexBufferCube;

        VolumeParams _volumeParams;
        Buffer _volumeParamBuffer;

        string _raycastEffectPath;
        string _modelEffectPath;

        public VolumeRenderer(RenderWindow Window)
            : base(Window)
        {
            _renderType = RenderType.Volume;
        }

        public VolumeParams VolumeParams
        {
            get { return _volumeParams; }
            set { _volumeParams = value; }
        }

        #region IDisposable
        ~VolumeRenderer()
        {
            Dispose(false);
        }
        private bool _disposed = false;
        new public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected override void Dispose(bool Disposing)
        {
            if (!_disposed)
            {
                if (Disposing)
                {

                }
                Disposer.RemoveAndDispose(ref _vsPosition);
                Disposer.RemoveAndDispose(ref _vsRaycast);
                Disposer.RemoveAndDispose(ref _ilPosition);
                Disposer.RemoveAndDispose(ref _ilRaycast);
                Disposer.RemoveAndDispose(ref _psPosition);
                Disposer.RemoveAndDispose(ref _psRaycast);

                Disposer.RemoveAndDispose(ref _texPositions);
                Disposer.RemoveAndDispose(ref _srvPositions);
                Disposer.RemoveAndDispose(ref _rtvPositions);

                Disposer.RemoveAndDispose(ref _samplerLinear);

                Disposer.RemoveAndDispose(ref _texVolumes);
                Disposer.RemoveAndDispose(ref _srvVolumes);

                Disposer.RemoveAndDispose(ref _texActiveVoxels);
                Disposer.RemoveAndDispose(ref _srvActiveVoxels);

                Disposer.RemoveAndDispose(ref _vertexBufferCube);
                Disposer.RemoveAndDispose(ref _indexBufferCube);

                Disposer.RemoveAndDispose(ref _volumeParamBuffer);

                _disposed = true;
            }
            base.Dispose(Disposing);
        }
        #endregion

        public override void InitializeRenderer()
        {
            _raycastEffectPath = @"Shaders\Raycast";
            _modelEffectPath = @"Shaders\Model";

            base.InitializeRenderer();
        }
        protected override void InitializeShaders()
        {
            DeviceContext context = _device.ImmediateContext;

            var vsByteCode = ShaderBytecode.FromFile(_modelEffectPath + ".vso");
            _vsPosition = new VertexShader(_device, vsByteCode);

            _ilPosition = new InputLayout(_device, ShaderSignature.GetInputSignature(vsByteCode), new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0)
            });

            var psByteCode = ShaderBytecode.FromFile(_modelEffectPath + ".pso");
            _psPosition = new PixelShader(_device, psByteCode);           

            vsByteCode = ShaderBytecode.FromFile(_raycastEffectPath + ".vso");
            _vsRaycast = new VertexShader(_device, vsByteCode);

            _ilRaycast = new InputLayout(_device, ShaderSignature.GetInputSignature(vsByteCode), new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0)
            });

            psByteCode = ShaderBytecode.FromFile(_raycastEffectPath + ".pso");
            _psRaycast = new PixelShader(_device, psByteCode);

            var gsByteCode = ShaderBytecode.FromFile(_modelEffectPath + ".gso");
            _gsPosition = new GeometryShader(_device, gsByteCode);

            gsByteCode = ShaderBytecode.FromFile(_raycastEffectPath + ".gso");
            _gsRaycast = new GeometryShader(_device, gsByteCode);
        }
        protected override void InitializeStates()
        {
            SamplerStateDescription desc = new SamplerStateDescription()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Border,
                ComparisonFunction = Comparison.Never,
                MinimumLod = 0,
                MaximumLod = float.MaxValue
            };
            _samplerLinear = new SamplerState(_device, desc);

            base.InitializeStates();
        }

        public void SetData(List<RenderVolume> Volumes)
        {
            //Create new or replace Texture3D so can be changed on fly

            _dataLoaded = false;

            if (_texVolumes != null)
            {
                for (int i = 0; i < _texVolumes.Length; i++)
                {
                    Disposer.RemoveAndDispose(ref _texVolumes[i]);
                }
            }
            if (_srvVolumes != null)
            {
                for (int i = 0; i < _srvVolumes.Length; i++)
                {
                    Disposer.RemoveAndDispose(ref _srvVolumes[i]);
                }
            }

            Disposer.RemoveAndDispose(ref _vertexBufferCube);
            Disposer.RemoveAndDispose(ref _indexBufferCube);
            Disposer.RemoveAndDispose(ref _boundingBox);
            Disposer.RemoveAndDispose(ref _coordinateBox);
            Disposer.RemoveAndDispose(ref _volumeParamBuffer);

            int width = -1;
            int height = -1;
            int depth = -1;
            foreach (RenderVolume volume in Volumes)
            {
                if (width == -1)
                {
                    width = volume.Width;
                }
                if (height == -1)
                {
                    height = volume.Height;
                }
                if (depth == -1)
                {
                    depth = volume.Depth;
                }

                if (volume.Width != width || volume.Height != height || volume.Depth != depth)
                {
                    throw new ArgumentException("Not all volumes are the same dimensions.");
                }
            }

            int numVolumes = Volumes.Count;

            _srvVolumes = new ShaderResourceView[numVolumes];
            _texVolumes = new Texture3D[numVolumes];

            _volumeParams = VolumeParams.Empty;

            for (int i = 0; i < numVolumes; i++)
            {
                _texVolumes[i] = Volumes[i].CreateTexture(_device, out _srvVolumes[i]);
                _volumeParams.UpdateColor(i, Volumes[i].Color.ToVector4());
            }

            _volumeParams.NumVolumes = (uint)numVolumes;

            _volumeParamBuffer = new Buffer(_device, Marshal.SizeOf(typeof(VolumeParams)), ResourceUsage.Default,
                BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);

            const float maxSize = 2.0f;
            float sizeX = maxSize;
            float sizeY = maxSize;
            float sizeZ = ((float)depth / (float)width) * maxSize;
            //sizeZ = sizeX;

            float startX = -sizeX / 2f;
            float startY = -sizeY / 2f;
            float startZ = -sizeZ / 2f;

            float maxValue = (float)Math.Max(sizeX, sizeY);
            maxValue = (float)Math.Max(maxValue, sizeZ);

            float ratioX = sizeX / (2 * maxValue);
            float ratioY = sizeY / (2 * maxValue);
            float ratioZ = sizeZ / (2 * maxValue);

            float sStartX = 0.5f - ratioX;
            float sStartY = 0.5f - ratioY;
            float sStartZ = 0.5f - ratioZ;

            float sEndX = 0.5f + ratioX;
            float sEndY = 0.5f + ratioY;
            float sEndZ = 0.5f + ratioZ;

            _volumeParams.VolumeScaleStart = new Vector4(sStartX, sStartY, sStartZ, 1.0f);

            _volumeParams.VolumeScaleDenominator = new Vector4(sEndX - sStartX, sEndY - sStartY, sEndZ - sStartZ, 1.0f);

            Vector4[] vertices = new Vector4[8] 
            {
                new Vector4(startX, startY, startZ, 1.0f),
                new Vector4(startX, startY, startZ + sizeZ, 1.0f),
                new Vector4(startX, startY + sizeY, startZ, 1.0f),
                new Vector4(startX, startY + sizeY, startZ + sizeZ, 1.0f),
                new Vector4(startX + sizeX, startY, startZ, 1.0f),
                new Vector4(startX + sizeX, startY, startZ + sizeZ, 1.0f),
                new Vector4(startX + sizeX, startY + sizeY, startZ, 1.0f),
                new Vector4(startX + sizeX, startY + sizeY, startZ + sizeZ, 1.0f)
            };

            _vertexBufferCube = Buffer.Create(_device, BindFlags.VertexBuffer, vertices);

            short[] indices = new short[36]
            {
                0, 1, 2,
                2, 1, 3,

                0, 4, 1,
                1, 4, 5,

                0, 2, 4,
                4, 2, 6,

                1, 5, 3,
                3, 5, 7,

                2, 3, 6,
                6, 3, 7,

                5, 4, 7,
                7, 4, 6,
            };

            _indexBufferCube = Buffer.Create(_device, BindFlags.IndexBuffer, indices);

            _boundingBox = new BoundingBox(_device, vertices);
            _coordinateBox = new CoordinateBox(_device, vertices);

            // Set up active voxel lookup volume
            int lSizeX = width / 3;
            if (width % 3 != 0) lSizeX += 1;
            int lSizeY = height / 3;
            if (height % 3 != 0) lSizeY += 1;
            int lSizeZ = depth / 3;
            if (depth % 3 != 0) lSizeZ += 1;

            bool[, ,] isActive = new bool[lSizeX, lSizeY, lSizeZ];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        int xCoord = x / 3;
                        int yCoord = y / 3;
                        int zCoord = z / 3;
                        for (int i = 0; i < numVolumes; i++)
                        {
                            if (Volumes[i][x, y, z] > 0)
                            {
                                isActive[xCoord, yCoord, zCoord] = true;
                                break;
                            }
                        }
                    }
                }
            }

            float[] activeVolume = new float[lSizeX * lSizeY * lSizeZ];
            int pos = 0;
            for (int x = 0; x < lSizeX; x++)
            {
                for (int y = 0; y < lSizeY; y++)
                {
                    for (int z = 0; z < lSizeZ; z++)
                    {
                        activeVolume[pos] = isActive[x, y, z] ? 1f : 0f;
                        pos++;
                    }
                }
            }

            Texture3DDescription desc = new Texture3DDescription()
            {
                Width = lSizeX,
                Height = lSizeY,
                Depth = lSizeZ,
                MipLevels = 1,
                Format = Format.R32_Float,
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None
            };
            DataStream stream = DataStream.Create<float>(activeVolume, true, true);
            _texActiveVoxels = new Texture3D(_device, desc, new[] { new DataBox(stream.DataPointer, lSizeX * sizeof(float), lSizeX * lSizeY * sizeof(float)) });

            _srvActiveVoxels = new ShaderResourceView(_device, _texActiveVoxels);

            _dataLoaded = true;
        }

        protected override bool Resize()
        {
            try
            {
                bool baseResized = base.Resize();
                if (!baseResized) return false;

                ShaderResourceViewDescription descSRV = new ShaderResourceViewDescription()
                {
                    Format = Format.R32G32B32A32_Float,
                    Dimension = ShaderResourceViewDimension.Texture2D
                };
                RenderTargetViewDescription descRTV = new RenderTargetViewDescription()
                {
                    Format = Format.R32G32B32A32_Float,
                    Dimension = RenderTargetViewDimension.Texture2D
                };

                _texPositions = new Texture2D[2];
                _srvPositions = new ShaderResourceView[2];
                _rtvPositions = new RenderTargetView[2];

                Texture2DDescription descTex = new Texture2DDescription()
                {
                    ArraySize = 1,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    Usage = ResourceUsage.Default,
                    Format = Format.R32G32B32A32_Float,
                    Width = _parent.ClientSize.Width,
                    Height = _parent.ClientSize.Height,
                    MipLevels = 1,
                    SampleDescription = new SampleDescription(1, 0),
                    CpuAccessFlags = CpuAccessFlags.None
                };
                for (int i = 0; i < 2; i++)
                {
                    Texture2D tex = new Texture2D(_device, descTex);

                    _texPositions[i] = tex;
                    _srvPositions[i] = new ShaderResourceView(_device, tex);
                    _rtvPositions[i] = new RenderTargetView(_device, tex);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override void Update(bool targetYAxisOrbiting)
        {

            base.Update(targetYAxisOrbiting);
        }

        public override void Draw()
        {
            base.Draw();

            if (_device == null) return;

            var context = _device.ImmediateContext;

            if (_dataLoaded)
            {
                _volumeParams.VolumeScale = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                for (int i = 0; i < _volumeParams.NumVolumes; i++)
                {
                    Vector4 current = _volumeParams.GetColor(i);
                    _volumeParams.UpdateColor(i, _dataContextRenderWindow.RenderWindowView.VolumeColors[i].ToVector4());
                }
                context.UpdateSubresource(ref _volumeParams, _volumeParamBuffer);

                context.InputAssembler.InputLayout = _ilPosition;
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBufferCube, Marshal.SizeOf(typeof(Vector4)), 0));
                context.InputAssembler.SetIndexBuffer(_indexBufferCube, Format.R16_UInt, 0);
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

                context.VertexShader.Set(_vsPosition);
                context.VertexShader.SetConstantBuffer(0, _bufferRenderParams);
                context.VertexShader.SetConstantBuffer(2, _volumeParamBuffer);
                context.GeometryShader.Set(_gsPosition);
                context.PixelShader.Set(_psPosition);

                // Set active voxel lookup volume for both the VS and PS
                context.VertexShader.SetShaderResources(2, 1, _srvActiveVoxels);
                context.PixelShader.SetShaderResources(2, 1, _srvActiveVoxels);

                context.Rasterizer.State = _rasterizerStateCullFront;
                context.OutputMerger.SetRenderTargets(null, _rtvPositions[1]);
                context.ClearRenderTargetView(_rtvPositions[1], Color.Black);
                context.DrawIndexed(36, 0, 0);

                context.Rasterizer.State = _rasterizerStateCullBack;
                context.OutputMerger.SetRenderTargets(null, _rtvPositions[0]);
                context.ClearRenderTargetView(_rtvPositions[0], Color.Black);
                context.DrawIndexed(36, 0, 0);

                context.VertexShader.Set(_vsRaycast);
                context.GeometryShader.Set(_gsRaycast);
                context.PixelShader.Set(_psRaycast);
                context.InputAssembler.InputLayout = _ilRaycast;                

                context.VertexShader.SetConstantBuffer(0, _bufferRenderParams);
                context.PixelShader.SetConstantBuffer(0, _bufferRenderParams);
                context.PixelShader.SetConstantBuffer(1, _bufferLightingParams);
                context.PixelShader.SetConstantBuffer(2, _volumeParamBuffer);

                context.OutputMerger.SetRenderTargets(_depthView, _renderView);

                //Set all volumes
                context.PixelShader.SetShaderResources(0, 1, _srvPositions[0]);
                context.PixelShader.SetShaderResources(1, 1, _srvPositions[1]);

                int startIndex = 3;
                for (int i = 0; i < _volumeParams.NumVolumes; i++)
                {
                    context.PixelShader.SetShaderResources(startIndex + i, 1, _srvVolumes[i]);
                }

                context.PixelShader.SetSamplers(0, 1, _samplerLinear);

                context.DrawIndexed(36, 0, 0);

                // Constant value for box
                _dataContextRenderWindow.RenderWindowView.NumTrianglesDrawn = 12;

                ShaderResourceView[] nullRV = new ShaderResourceView[3] { null, null, null };
                context.PixelShader.SetShaderResources(0, 3, nullRV);

                // Clear geometry shader since axes/bounding box don't use it
                context.GeometryShader.Set(null);
            }

            CompleteDraw();
        }
    }
}