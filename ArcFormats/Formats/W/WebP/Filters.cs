namespace GameRes.Formats.Google
{
    internal static class WebpFilter // Filter types.
    {
        public const int None = 0;
        public const int Horizontal = 1;
        public const int Vertical = 2;
        public const int Gradient = 3;
        public const int Last = Gradient + 1; // end marker
        public const int Best = Last + 1; // meta-types
        public const int Fast = Best + 1;

        static void PredictLine (byte[] input, int src, byte[] preds, int pred,
                                 byte[] output, int dst, int length, bool inverse)
        {
            if (inverse)
            {
                for (int i = 0; i < length; ++i)
                    output[dst+i] = (byte)(input[src+i] + preds[pred+i]);
            }
            else
            {
                for (int i = 0; i < length; ++i)
                    output[dst+i] = (byte)(input[src+i] - preds[pred+i]);
            }
        }

        //------------------------------------------------------------------------------
        // Horizontal filter.

        static void DoHorizontalFilter (byte[] input, int src, int width, int height,
                                        int stride, int row, int num_rows, bool inverse,
                                        byte[] output, int dst)
        {
            int start_offset = row * stride;
            int last_row = row + num_rows;
            src += start_offset;
            dst += start_offset;
            var preds = inverse ? output : input;
            var pred = inverse ? dst : src;

            if (0 == row)
            {
                // Leftmost pixel is the same as input for topmost scanline.
                output[dst] = input[src];
                PredictLine (input, src + 1, preds, pred, output, dst + 1, width - 1, inverse);
                row = 1;
                pred += stride;
                src += stride;
                dst += stride;
            }

            // Filter line-by-line.
            while (row < last_row)
            {
                // Leftmost pixel is predicted from above.
                PredictLine (input, src, preds, pred - stride, output, dst, 1, inverse);
                PredictLine (input, src + 1, preds, pred, output, dst + 1, width - 1, inverse);
                ++row;
                pred += stride;
                src += stride;
                dst += stride;
            }
        }

        //------------------------------------------------------------------------------
        // Vertical filter.

        static void DoVerticalFilter (byte[] input, int src, int width, int height,
                                      int stride, int row, int num_rows, bool inverse,
                                      byte[] output, int dst)
        {
            int start_offset = row * stride;
            int last_row = row + num_rows;
            src += start_offset;
            dst += start_offset;
            var preds = inverse ? output : input;
            int pred = inverse ? dst : src;

            if (0 == row)
            {
                // Very first top-left pixel is copied.
                output[dst] = input[src];
                // Rest of top scan-line is left-predicted.
                PredictLine (input, src + 1, preds, pred, output, dst + 1, width - 1, inverse);
                row = 1;
                src += stride;
                dst += stride;
            }
            else
            {
                // We are starting from in-between. Make sure 'preds' points to prev row.
                pred -= stride;
            }

            // Filter line-by-line.
            while (row < last_row)
            {
                PredictLine (input, src, preds, pred, output, dst, width, inverse);
                ++row;
                pred += stride;
                src += stride;
                dst += stride;
            }
        }

        //------------------------------------------------------------------------------
        // Gradient filter.

        static int GradientPredictor (byte a, byte b, byte c)
        {
            int g = a + b - c;
            return ((g & ~0xFF) == 0) ? g : (g < 0) ? 0 : 255;  // clip to 8bit
        }

        static void DoGradientFilter (byte[] input, int src, int width, int height,
                                      int stride, int row, int num_rows, bool inverse,
                                      byte[] output, int dst)
        {
            int start_offset = row * stride;
            int last_row = row + num_rows;
            src += start_offset;
            dst += start_offset;
            var preds = inverse ? output : input;
            int pred = inverse ? dst : src;

            // left prediction for top scan-line
            if (0 == row)
            {
                output[dst] = input[src];
                PredictLine (input, src + 1, preds, pred, output, dst + 1, width - 1, inverse);
                row = 1;
                pred += stride;
                src += stride;
                dst += stride;
            }

            // Filter line-by-line.
            while (row < last_row)
            {
                // leftmost pixel: predict from above.
                PredictLine (input, src, preds, pred - stride, output, dst, 1, inverse);
                for (int w = 1; w < width; ++w)
                {
                    int p = GradientPredictor (preds[pred + w - 1], preds[pred + w - stride], preds[pred + w - stride - 1]);
                    output[dst+w] = (byte)(input[src+w] + (inverse ? p : -p));
                }
                ++row;
                pred += stride;
                src += stride;
                dst += stride;
            }
        }

        //------------------------------------------------------------------------------

        public static void HorizontalFilter (byte[] data, int src, int width, int height,
                                             int stride, byte[] filtered_data, int dst)
        {
            DoHorizontalFilter (data, src, width, height, stride, 0, height, false, filtered_data, dst);
        }

        public static void VerticalFilter (byte[] data, int src, int width, int height,
                                           int stride, byte[] filtered_data, int dst)
        {
            DoVerticalFilter (data, src, width, height, stride, 0, height, false, filtered_data, dst);
        }


        public static void GradientFilter (byte[] data, int src, int width, int height,
                                           int stride, byte[] filtered_data, int dst)
        {
            DoGradientFilter (data, src, width, height, stride, 0, height, false, filtered_data, dst);
        }


        //------------------------------------------------------------------------------

        delegate void UnfilterFunc (int width, int height, int stride, int row,
                                    int num_rows, byte[] data, int dst);

        public static void VerticalUnfilter (int width, int height, int stride, int row,
                                             int num_rows, byte[] data, int src)
        {
            DoVerticalFilter (data, src, width, height, stride, row, num_rows, true, data, src);
        }

        public static void HorizontalUnfilter (int width, int height, int stride, int row,
                                               int num_rows, byte[] data, int src)
        {
            DoHorizontalFilter (data, src, width, height, stride, row, num_rows, true, data, src);
        }

        public static void GradientUnfilter (int width, int height, int stride, int row,
                                             int num_rows, byte[] data, int src)
        {
            DoGradientFilter (data, src, width, height, stride, row, num_rows, true, data, src);
        }
    }
}
