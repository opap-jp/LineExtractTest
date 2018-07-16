using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace LineExtractTest
{
    public class LineExtractProcessor
    {

        const int CHANNEL_A = 3;
        const int CHANNEL_R = 2;
        const int CHANNEL_G = 1;
        const int CHANNEL_B = 0;

        const int NEIGHBORS_TOPLEFT = 0;
        const int NEIGHBORS_TOP = 1;
        const int NEIGHBORS_TOPRIGHT = 2;
        const int NEIGHBORS_LEFT = 3;
        const int NEIGHBORS_CENTER = 4;
        const int NEIGHBORS_RIGHT = 5;
        const int NEIGHBORS_BOTTOMLEFT = 6;
        const int NEIGHBORS_BOTTOM = 7;
        const int NEIGHBORS_BOTTOMRIGHT = 8;

        private int _Threshold = 32;

      　readonly Vector<int> OneAndAlphaZero = new Vector<int>(new int[] { 1, 1, 1, 0 });
        readonly Vector<int> DummyAlpha = new Vector<int>(new int[] { 0, 0, 0, 1 });

        readonly Vector<int> BrightnessChannelFactors = new Vector<int>(new int[] {
                (int)(0.298912 * 1024),
                (int)(0.586611 * 1024),
                (int)(0.114478 * 1024),
                0
        });

        private Bitmap _InputImage;



        public int Threshold
        {
            get { return _Threshold; }
            set { _Threshold = value; }
        }


        public LineExtractProcessor(Bitmap inputImage)
        {
            this._InputImage = inputImage;
        }

        public void ExtractLineArt(Bitmap outputImage)
        {

            BitmapData inputData = null;
            BitmapData outputdata = null;
            try
            {

                inputData = _InputImage.LockBits(new Rectangle(Point.Empty, _InputImage.Size), System.Drawing.Imaging.ImageLockMode.ReadWrite, _InputImage.PixelFormat);
                outputdata = outputImage.LockBits(new Rectangle(Point.Empty, _InputImage.Size), System.Drawing.Imaging.ImageLockMode.ReadWrite, _InputImage.PixelFormat);

                unsafe
                {
                    byte* inputPtrStart = (byte*)inputData.Scan0;
                    byte* outputPtrStart = (byte*)outputdata.Scan0;

                    int channelCount = inputData.Stride / inputData.Width;
                    int stride = inputData.Stride;
                    int w = inputData.Width;
                    int h = inputData.Height;

                    int pixelCount = w * h;

                    /* 何度も計算するため予め輝度を計算しておく */
                    int[] glaydata = new int[pixelCount];
                    Parallel.For(0, pixelCount, i =>
                    {
                        glaydata[i] = GetBrightness(
                            ReadPixelDataToVector(inputPtrStart + i * channelCount)
                        );
                    });
                    
                    /* 線画抽出処理 */
                    Parallel.For(0, pixelCount, i =>
                    {
                        int y = i / w;
                        int x = i - y * w;

                        Vector<int>[] neighbors = new Vector<int>[9];
                        int[] neighborsBrightness = new int[9];

                        byte* inputPtr = inputPtrStart + i * channelCount;
                        byte* outputPtr = outputPtrStart + i * channelCount;

                        Vector<int> srcPixel = ReadPixelDataToVector(inputPtr);

                        ReadNeighborsToVectorArray(inputPtr, x, y, w, h, stride, channelCount, ref neighbors);
                        ReadNeighborsToArray(ref glaydata, i, x, y, w, h, ref neighborsBrightness);


                        outputPtr[CHANNEL_A] = 0;
                        outputPtr[CHANNEL_R] = 0;
                        outputPtr[CHANNEL_G] = 0;
                        outputPtr[CHANNEL_B] = 0;

                        int br = GetBrightness(srcPixel);

                        if (br < _Threshold)
                        {
                            outputPtr[CHANNEL_A] = (byte)Math.Min(srcPixel[CHANNEL_A], 255 - br);
                        }
                        else
                        {
                            int guessedAlpha = GuessAlphaFromNeighbors(srcPixel, neighbors, neighborsBrightness);
                            if (guessedAlpha >= 0)
                            {
                                outputPtr[CHANNEL_A] = (byte)Math.Min(Math.Min(srcPixel[CHANNEL_A], guessedAlpha), 255);
                            }
                        }


                    });

                }

            }
            finally
            {
                if (inputData != null) { _InputImage.UnlockBits(inputData); }
                if (outputdata != null) { outputImage.UnlockBits(outputdata); }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSaturation(Vector<int> argb)
        {
            int min = Math.Min(Math.Min(argb[CHANNEL_R], argb[CHANNEL_G]), argb[CHANNEL_B]);
            int max = Math.Max(Math.Max(argb[CHANNEL_R], argb[CHANNEL_G]), argb[CHANNEL_B]);
            if (max == 0) return 0;

            int saturation = 255 * (max - min) / max;
            return saturation;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBrightness(Vector<int> argb)
        {

            int brightness = Vector.Dot(argb, BrightnessChannelFactors) >> 10;
            return brightness;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GuessAlphaFromNeighbors(Vector<int> srcPixel, Vector<int>[] neighbors, int[] neighborsBrightness)
        {
            int br = GetBrightness(srcPixel);

            int maxbr = neighborsBrightness.Max();
            int minbr = neighborsBrightness.Min();

            if (minbr < _Threshold && br < maxbr && minbr < maxbr)
            {
                Vector<int> max = neighbors
                                    .OrderBy(n => GetBrightness(n))
                                    .ThenBy(n => GetSaturation(n)).Last();

                
                if (minbr < maxbr && Vector.LessThanOrEqualAll(srcPixel * OneAndAlphaZero, max * OneAndAlphaZero))
                {
                    int guessedAlpha = 255 * (br - maxbr) / (minbr - maxbr);
                    return guessedAlpha;
                }
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe Vector<int> ReadPixelDataToVector(byte* data)
        {
            return new Vector<int>(new int[] { data[0], data[1], data[2], data[3] });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void ReadNeighborsToVectorArray(byte* srcPtr, int x, int y, int w, int h, int stride, int channelCount, ref Vector<int>[] neighbors)
        {
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int nx = x + j - 1;
                    int ny = y + i - 1;

                    if (nx < 0) { nx = 0; }
                    if (nx >= w) { nx = w - 1; }
                    if (ny < 0) { ny = 0; }
                    if (ny >=h) { ny = h-1; }

                    neighbors[i * 3 + j] =ReadPixelDataToVector( srcPtr - stride*y-x+stride*ny+x)
                        ;


                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void ReadNeighborsToArray(ref int[] data, int index, int x, int y, int w, int h, ref int[] neighbors)
        {

            for(int i=0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int nx = x + j - 1;
                    int ny = y + i - 1;

                    if(nx < 0) { nx = 0; }
                    if(nx >= w) { nx = w - 1; }
                    if (ny < 0) { ny = 0; }
                    if (ny >= h) { ny = h -1; }

                    neighbors[i * 3 + j] = data[(ny) * w + (nx)];

                }
            } 
        }



    }
}
