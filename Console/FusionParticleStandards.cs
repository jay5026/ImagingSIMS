﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImagingSIMS.Common;
using ImagingSIMS.Data;
using ImagingSIMS.Data.Imaging;

namespace ConsoleApp
{
    public class FusionParticleStandards
    {
        public static void Run()
        {
            var outputFolder = "D:\\Data\\FusionParticles\\";

            List<SIMSParticle> simsParticles = new List<SIMSParticle>();
            List<SEMParticle> semParticles = new List<SEMParticle>();

            // Based on 256 x 256 pixels FOV
            int imageSize = 256;
            int hrFactor = 2;
            int[] radii = { 5, 8, 10, 15, 20, 25, 30, 40, 50, 65 };
            int[] centerX = { 240, 225, 150, 115, 115, 170, 200, 50, 190, 70 };
            int[] centerY = { 200, 225, 65, 85, 25, 225, 40, 50, 140, 180 };

            //List<Tuple<int, int, int>> particleInfos = new List<Tuple<int, int, int>>()
            //{
            //    new Tuple<int, int, int>(5, 240, 200),
            //    new Tuple<int, int, int>(8, 225, 225),
            //    new Tuple<int, int, int>(10, 150, 65),
            //    new Tuple<int, int, int>(15, 115, 85),
            //    new Tuple<int, int, int>(20, 115, 25),
            //    new Tuple<int, int, int>(25, 170, 225),
            //    new Tuple<int, int, int>(30, 200, 40),
            //    new Tuple<int, int, int>(40, 50, 50),
            //    new Tuple<int, int, int>(50, 190, 140),
            //    new Tuple<int, int, int>(65, 70, 180)
            //};

            List<Tuple<int, int, int>> particleInfos = new List<Tuple<int, int, int>>()
            {
                // Row 1
                new Tuple<int, int, int>(2, 30, 30),
                new Tuple<int, int, int>(3, 95, 30),
                new Tuple<int, int, int>(4, 160, 30),
                new Tuple<int, int, int>(5, 225, 30),
                
                // Row 2
                new Tuple<int, int, int>(9, 225, 90),
                new Tuple<int, int, int>(8, 160, 90),
                new Tuple<int, int, int>(7, 95, 90),
                new Tuple<int, int, int>(6, 30, 90),

                // Row 3
                new Tuple<int, int, int>(10, 50, 150),
                new Tuple<int, int, int>(12, 120, 150),
                new Tuple<int, int, int>(14, 190, 150),

                // Row 4
                new Tuple<int, int, int>(20, 220, 220),
                new Tuple<int, int, int>(18, 105, 220),
                new Tuple<int, int, int>(16, 30, 220),
            };

            //double maxRadius = radii.Max();
            //double minRadius = radii.Min();

            foreach (var info in particleInfos)
            {
                int radius = (int)(0.936 * info.Item1 + 24.464);
                simsParticles.Add(new SIMSParticle(radius, info.Item2, info.Item3, imageSize, imageSize));                
                semParticles.Add(new SEMParticle(info.Item1 * hrFactor, info.Item2 * hrFactor, info.Item3 * hrFactor, imageSize * hrFactor, imageSize * hrFactor));
            }

            var simsData = new Data2D(imageSize, imageSize);
            var semData = new Data2D(imageSize * hrFactor, imageSize * hrFactor);

            foreach (var particle in simsParticles)
            {
                particle.GeneratePixels();
                simsData += particle.Matrix;
            }
            foreach (var particle in semParticles)
            {
                particle.GeneratePixels();
                semData += particle.Matrix;
            }

            var bsSIMS = ImageGenerator.Instance.Create(simsData, ColorScaleTypes.Neon);
            var bsSEM = ImageGenerator.Instance.Create(semData, ColorScaleTypes.Gray);

            bsSIMS.Save(outputFolder + "SIMS.bmp");
            bsSEM.Save(outputFolder + "SEM.bmp");           
        }

        private static bool IsInCircle(int x, int y, int cX, int cY, int radius)
        {
            return Math.Sqrt(Math.Pow(cX - x, 2) + Math.Pow(cY - y, 2)) <= radius;
        }
    }

    internal static class RandomGenerator
    {
        static Random _random = new Random(12091986);

        public static int Next()
        {
            return _random.Next();
        }
        public static int Next(int max)
        {
            return _random.Next(max);
        }
        public static int Next(int min, int max)
        {
            return _random.Next(min, max);
        }
    }

    internal abstract class Particle
    {
        public int ImageSizeX { get; set; }
        public int ImageSizeY { get; set; }
        public int Radius { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int TotalCounts { get; set; }
        public Data2D Matrix { get; set; }

        public Particle(int radius, int centerX, int centerY, int imageSizeX, int imageSizeY)
        {
            Radius = radius;
            CenterX = centerX;
            CenterY = centerY;
            ImageSizeX = imageSizeX;
            ImageSizeY = imageSizeY;

            Matrix = new Data2D(imageSizeX, imageSizeY);
        }

        public abstract void GeneratePixels();

        protected static bool IsInCircle(int x, int y, int cX, int cY, int radius)
        {
            return Math.Sqrt(Math.Pow(cX - x, 2) + Math.Pow(cY - y, 2)) <= radius;
        }
    }

    internal class SIMSParticle : Particle
    {
        public SIMSParticle(int radius, int centerX, int centerY, int imageSizeX, int imageSizeY) 
            : base(radius, centerX, centerY, imageSizeX, imageSizeY)
        {

            //for (int i = 0; i < 100; i++)
            //{
            //    double distance  = i / 100d;
            //    Console.WriteLine($"i {i} distance {Gaussian(distance, 0, 0.2)}");
            //}
        }

        public override void GeneratePixels()
        {
            int idealCounts = (int)(Math.PI * Math.Pow(Radius, 2) * RandomGenerator.Next(50, 80));

            double sigmaSquared = .2;
            double mu = 0;

            while(idealCounts > 0)
            {
                int x = RandomGenerator.Next(CenterX - Radius, CenterX + Radius);
                int y = RandomGenerator.Next(CenterY - Radius, CenterY + Radius);

                if (!IsInCircle(x, y, CenterX, CenterY, Radius)) continue;
                if (x < 0 || x >= ImageSizeX || y < 0 || y >= ImageSizeY) continue;

                int counts = RandomGenerator.Next(10, 30);
                counts += RandomGenerator.Next(-(int)Math.Sqrt(counts), (int)Math.Sqrt(counts));

                double distanceToCenter = Math.Sqrt(Math.Pow(CenterX - x, 2) + Math.Pow(CenterY - y, 2)) / Radius;
                //counts = (int)(counts / Math.Max(Math.Min(distanceToCenter, 1.3), 0.5));
                counts = (int)(counts * Gaussian(distanceToCenter, mu, sigmaSquared));

                idealCounts -= counts;
                Matrix[x, y] += counts;
            }

            TotalCounts = (int)Matrix.TotalCounts;
        }

        private double Gaussian(double x, double mu, double sigmaSquared)
        {
            //return (1 / Math.Sqrt(2 * Math.PI * sigmaSquared)) * Math.Pow(Math.E, -(Math.Pow(x - mu, 2) / 2 * sigmaSquared));
            return (Math.Pow(Math.E, -Math.Pow(x - mu, 2)/(2*sigmaSquared))/Math.Sqrt(2*Math.PI*sigmaSquared));
        }
    }

    internal class SEMParticle : SIMSParticle
    {
        public SEMParticle(int radius, int centerX, int centerY, int imageSizeX, int imageSizeY) 
            : base(radius, centerX, centerY, imageSizeX, imageSizeY)
        {
        }

        public override void GeneratePixels()
        {
            for (int x = 0; x < ImageSizeX; x++)
            {
                for (int y = 0; y < ImageSizeY; y++)
                {
                    if (!IsInCircle(x, y, CenterX, CenterY, Radius)) continue;

                    double distanceToCenter = Math.Sqrt(Math.Pow(CenterX - x, 2) + Math.Pow(CenterY - y, 2)) / Radius;
                    int counts = (int)(Math.Sqrt(1 - Math.Pow(distanceToCenter, 2)) * 1000);

                    Matrix[x, y] = counts;
                }
            }

            TotalCounts = (int)Matrix.TotalCounts;
        }
    }
}
