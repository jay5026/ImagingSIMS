﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;

namespace ImagingSIMS.Data.Imaging
{
    public static class ImageGenerator
    {
        static IImageGenerator _generator;

        static ImageGenerator()
        {
            if (SupportsComputShader())
                _generator = new GPUImageGenerator();
            else _generator = new CPUImageGenerator();
        }

        static bool SupportsComputShader()
        {
            Device device = new Device(DriverType.Hardware, DeviceCreationFlags.None);
            return device.CheckFeatureSupport(Feature.ComputeShaders);
        }
    }
}