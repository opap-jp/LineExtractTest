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
        private int GuessAlpha(Vector<int> background, Vector<int> lineArtLayer, Vector<int> result, out int penalty)
        {
            //////////////// アルファ値を推測する ////////////////
            

            Vector<int> guessedAlpha =  255 * 1024 * (background - result) / ((background - lineArtLayer) * 1024 + Vector<int>.One /*ゼロ除算回避用*/);


            //アルファ値が計算に含まれないようゼロにする
            guessedAlpha = guessedAlpha * OneAndAlphaZero;


            //////////////// 不確かさを表すヒューリスティック値を算出 ////////////////

            penalty = 0;

            
            //// そもそも正しく計算できていない可能性が高い場合 ////

            //255を超えるものが含まれる場合
            if (Vector.GreaterThanAny(guessedAlpha, Vector<int>.One * 255))
            {
                penalty += 1000000;
            }

            //0未満のものが含まれる場合
            if (Vector.LessThanAny(guessedAlpha, Vector<int>.Zero))
            {
                penalty += 1000000;
            }

            //いずれかの色で背景よりも線画レイヤの方がRGB値が高い場合
            penalty += Vector.Dot( Vector.GreaterThan(lineArtLayer, background), Vector<int>.One * 10000);
            
            //いずれかの色で背景よりも合成結果の方がRGB値が高い場合
            penalty += Vector.Dot( Vector.GreaterThan(result, background), Vector<int>.One * 10000);
            
            //// 値に応じたペナルティ ////

            //色別で推測結果にばらつきが大きいほどマイナス評価
            int sum = Vector.Dot(guessedAlpha, OneAndAlphaZero);
            int avg = sum / 3;

            int sum2 = Vector.Dot(guessedAlpha, guessedAlpha);
            int sum2avg = sum2 / 3;
            int sd = (int)Math.Sqrt(sum2avg - avg * avg);

            penalty += 32 * sd;

            //線画が明るいほどマイナス評価
            //penalty += 64 * layerBr;

            //背景が暗いほどマイナス評価
            penalty += 32 * (255 - GetBrightness(background));

            //背景の彩度が低いほどマイナス評価
            penalty += 48 * (255 - GetSaturation(background));


            //線画と背景の差（の2乗平均）が小さいほどマイナス評価 
            Vector<int> diff_pow2 = ((background - lineArtLayer) * (background - lineArtLayer));
            Vector4 diff_sqrt = Vector4.SquareRoot(new Vector4(diff_pow2[0], diff_pow2[1], diff_pow2[2], 0));
            penalty += (int)Vector4.Dot(diff_sqrt, Vector4.One);
            

            return avg;

        }
        int[] neighborsIndexes = {0, 1, 2, 3, 5, 6, 7, 8};

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GuessAlphaFromNeighbors2(Vector<int> srcPixel, Vector<int>[] neighbors, int[] neighborsBrightness)
        {
            int minBr = neighborsIndexes.Select(x => neighborsBrightness[x]).Min();
            
            if (minBr > _Threshold)
            {
                return -1;
            }

            int minPenalty = int.MaxValue;
            int guessedAlpha = -1;
            
            Vector<int> layer = neighborsIndexes.Select(x=> neighbors[x]).OrderBy(x => GetBrightness(x)).First();
            Vector<int> result = srcPixel;
            int layer_bright = minBr;
            int result_bright = neighborsBrightness[NEIGHBORS_CENTER];

            foreach (int index in neighborsIndexes)
            {
                Vector<int> background = neighbors[index];
                int background_bright = neighborsBrightness[index];


                if (layer_bright < _Threshold && layer_bright < result_bright && result_bright < background_bright)
                {
                    int penalty;
                    int aa = GuessAlpha(background, layer, result, out penalty);

                    if (penalty < minPenalty)
                    {
                        minPenalty = penalty;
                        guessedAlpha = aa;
                    }
                }

            }

            return guessedAlpha;
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
