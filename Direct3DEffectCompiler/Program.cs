﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Direct3DEffectCompiler
{
    internal static class FilePaths
    {
        internal static string executablePath = @"C:\Program Files (x86)\Windows Kits\10\bin\x64\fxc.exe";
        internal static string sourceFolder = @"C:\Users\taro148\Source\Repos\ImagingSIMS\Direct3DRendering\Shaders\";
        internal static string outputFolder = @"C:\Users\taro148\Source\Repos\ImagingSIMS\Direct3DRendering\Shaders\";
        //internal static string sourceFolder = @"C:\Users\jay50\Source\Repos\jay5026\ImagingSIMS3\ImagingSIMS\Direct3DRendering\Shaders\";
        //internal static string outputFolder = @"C:\Users\jay50\Source\Repos\jay5026\ImagingSIMS3\ImagingSIMS\Direct3DRendering\Shaders\";
        //internal static _outputFolder = @"C:\Users\jayt\Desktop\test\";
    }
    class Program
    {
        static void Main(string[] args)
        {
            // Check args to see if one or more paths were specified
            if (args.Length > 0)
            {
                if (args.Length >= 1)
                {
                    FilePaths.executablePath = args[0];
                }
                if (args.Length >= 2)
                {
                    FilePaths.sourceFolder = args[1];
                }
                if (args.Length >= 3)
                {
                    FilePaths.outputFolder = args[2];
                }
            }

            if (!File.Exists(FilePaths.executablePath))
            {
                Console.WriteLine("FXC.exe not found.");
                return;
            }
            if (!Directory.Exists(Path.GetDirectoryName(FilePaths.sourceFolder)))
            {
                Console.WriteLine("Could not find directory containing shaders.");
                return;
            }
            if (!Directory.Exists(Path.GetDirectoryName(FilePaths.outputFolder)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePaths.outputFolder));
            }

            const int numShaders = 7;

            string[,] shaders = new string[numShaders, 2]
            {
                {"Axes", "Axes"},
                {"BoundingBox", "BoundingBox"},
                {"CoordinateBox", "CoordinateBox"},
                {"HeightMap", "HeightMap"},
                {"Isosurface", "Volume"},
                {"Model", "Volume"},
                {"Raycast", "Volume"}
            };

            int errorCount = 0;
            for (int i = 0; i < numShaders; i++)
            {
                Shader ps = new Shader(shaders[i, 0], shaders[i, 1], ShaderType.Pixel);
                if (!ps.Compile(true)) 
                    errorCount++;
                if (!ps.Compile(false))
                    errorCount++;

                Shader vs = new Shader(shaders[i, 0], shaders[i, 1], ShaderType.Vertex);
                if (!vs.Compile(true)) 
                    errorCount++;
                if (!vs.Compile(false))
                    errorCount++;

                if(shaders[i, 0] == "Isosurface" || shaders[i, 0] == "Raycast" || shaders[i, 0] == "Model")
                {
                    Shader gs = new Shader(shaders[i, 0], shaders[i, 1], ShaderType.Geometry);
                    if (!gs.Compile(true))
                        errorCount++;
                    if (!gs.Compile(false))
                        errorCount++;
                }
                
            }

            if (errorCount > 0)
            {
                Console.WriteLine(string.Format("There were {0} errors compiling the shaders.", errorCount));
                Console.ReadLine();
            }
        }
    }

    internal class Shader
    {
        string _shaderName;
        ShaderType _shaderType;
        string _hlslName;

        public Shader(string shaderName, string hlslName, ShaderType shaderType)
        {
            this._shaderName = shaderName;
            this._shaderType = shaderType;
            this._hlslName = hlslName;
        }

        public bool Compile(bool isDebug)
        {
            var arguments = generateArguments(isDebug);

            var info = new ProcessStartInfo(FilePaths.executablePath, arguments)
            {
                CreateNoWindow = true,
                ErrorDialog = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var proc = Process.Start(info);
            var procResult = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode == 0)
            {
                Console.WriteLine(string.Format("Shader {0} ({1}{2}) has been compiled successfully.", 
                    _shaderName, _shaderType.ToString(), (isDebug ? "-Debug" : "")));
            }
            else
            {
                Console.WriteLine(string.Format("Could not compile shader {0} ({1}{2}):",
                    _shaderName, _shaderType.ToString(), (isDebug ? "-Debug" : "")));
                Console.WriteLine(procResult);
            }

            return proc.ExitCode == 0;
        }
        private string generateArguments(bool isDebug)
        {
            StringBuilder sb = new StringBuilder();

            if (isDebug)
            {
                sb.Append("/Od ");
                sb.Append("/Zi ");
            }

            sb.Append("/T ");

            switch (_shaderType)
            {
                case ShaderType.Pixel:
                    sb.Append("ps_4_0 ");
                    break;
                case ShaderType.Vertex:
                    sb.Append("vs_4_0 ");
                    break;
                case ShaderType.Geometry:
                    sb.Append("gs_4_0 ");
                    break;
            }

            sb.Append("/E ");
            sb.Append(generateEntryMethod());

            sb.Append("/Fo ");
            sb.Append(generateFileName(isDebug));

            if (isDebug)
            {
                sb.Append("/Fd ");
                sb.Append(generatePDBName());
            }

            sb.Append(FilePaths.sourceFolder + _hlslName + ".hlsl ");

            return sb.ToString();
        }

        private string generateFileName(bool isDebug)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FilePaths.outputFolder);

            if (!isDebug)
                sb.Append(@"Release\");

            sb.Append(_shaderName);

            switch (_shaderType)
            {
                case ShaderType.Pixel:
                    sb.Append(".pso");
                    break;
                case ShaderType.Vertex:
                    sb.Append(".vso");
                    break;
                case ShaderType.Geometry:
                    sb.Append(".gso");
                    break;
            }

            return string.Format("\"{0}\" ", sb.ToString());
        }
        private string generatePDBName()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FilePaths.outputFolder);

            sb.Append(_shaderName);

            switch (_shaderType)
            {
                case ShaderType.Pixel:
                    sb.Append(".pso");
                    break;
                case ShaderType.Vertex:
                    sb.Append(".vso");
                    break;
                case ShaderType.Geometry:
                    sb.Append(".gso");
                    break;
            }

            sb.Append(".pdb");

            return string.Format("\"{0}\" ", sb.ToString());

        }
        private string generateEntryMethod()
        {
            StringBuilder sb = new StringBuilder();

            if(_shaderName == "BoundingBox")            
                sb.Append("BBOX");            
            else if (_shaderName == "CoordinateBox")            
                sb.Append("CBOX");            
            else 
                sb.Append(_shaderName.ToUpper());

            switch (_shaderType)
            {
                case ShaderType.Pixel:
                    sb.Append("_PS ");
                    break;
                case ShaderType.Vertex:
                    sb.Append("_VS ");
                    break;
                case ShaderType.Geometry:
                    sb.Append("_GS ");
                    break;
            }

            return sb.ToString();
        }
    }

    internal enum ShaderType
    {
        Pixel,
        Vertex,
        Geometry
    }
}
