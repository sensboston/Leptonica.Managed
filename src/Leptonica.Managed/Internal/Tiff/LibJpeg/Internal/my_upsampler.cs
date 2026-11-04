#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
/*
 * This file contains upsampling routines.
 *
 * Upsampling input data is counted in "row groups".  A row group
 * is defined to be (v_samp_factor * DCT_v_scaled_size / min_DCT_v_scaled_size)
 * sample rows of each component.  Upsampling will normally produce
 * max_v_samp_factor pixel rows from each row group (but this could vary
 * if the upsampler is applying a scale factor of its own).
 *
 * An excellent reference for image resampling is
 *   Digital Image Warping, George Wolberg, 1990.
 *   Pub. by IEEE Computer Society Press, Los Alamitos, CA. ISBN 0-8186-8944-7.
 */

using System.Runtime.Intrinsics;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    class my_upsampler : jpeg_upsampler
    {
        private enum ComponentUpsampler
        {
            noop_upsampler,
            fullsize_upsampler,
            h2v1_upsampler,
            h2v2_upsampler,
            int_upsampler,
            h2v1_fancy_upsampler,
            h1v2_fancy_upsampler,
            h2v2_fancy_upsampler
        }

        private jpeg_decompress_struct m_cinfo;

        /* Color conversion buffer.  When using separate upsampling and color
        * conversion steps, this buffer holds one upsampled row group until it
        * has been color converted and output.
        * Note: we do not allocate any storage for component(s) which are full-size,
        * ie do not need rescaling.  The corresponding entry of color_buf[] is
        * simply set to point to the input data array, thereby avoiding copying.
        */
        private ComponentBuffer[] m_color_buf = new ComponentBuffer[JpegConstants.MAX_COMPONENTS];

        // used only for fullsize_upsampler mode
        private int[] m_perComponentOffsets = new int[JpegConstants.MAX_COMPONENTS];

        /* Per-component upsampling method pointers */
        private ComponentUpsampler[] m_upsampleMethods = new ComponentUpsampler[JpegConstants.MAX_COMPONENTS];
        private int m_currentComponent; // component being upsampled
        private int m_upsampleRowOffset;
        
        private int m_next_row_out;       /* counts rows emitted from color_buf */
        private int m_rows_to_go;  /* counts rows remaining in image */

        /* Height of an input row group for each component. */
        private int[] m_rowgroup_height = new int[JpegConstants.MAX_COMPONENTS];

        /* These arrays save pixel expansion factors so that int_expand need not
        * recompute them each time.  They are unused for other upsampling methods.
        */
        private byte[] m_h_expand = new byte[JpegConstants.MAX_COMPONENTS];
        private byte[] m_v_expand = new byte[JpegConstants.MAX_COMPONENTS];

        public my_upsampler(jpeg_decompress_struct cinfo)
        {
            m_cinfo = cinfo;
            m_need_context_rows = false; /* until we find out differently */

            if (cinfo.m_CCIR601_sampling)    /* this isn't supported */
                cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CCIR601_NOTIMPL);

            /* The main controller doesn't support context rows when
             * min_DCT_scaled_size = 1, so fancy upsampling is not used then
             * (same rule as libjpeg-turbo).
             */
            bool do_fancy = cinfo.m_do_fancy_upsampling && cinfo.min_DCT_h_scaled_size > 1;

            /* Verify we can handle the sampling factors, select per-component methods,
            * and create storage as needed.
            */
            for (int ci = 0; ci < cinfo.m_num_components; ci++)
            {
                jpeg_component_info componentInfo = cinfo.Comp_info[ci];

                /* Compute size of an "input group" after IDCT scaling.  This many samples
                * are to be converted to max_h_samp_factor * max_v_samp_factor pixels.
                */
                int h_in_group = (componentInfo.H_samp_factor * componentInfo.DCT_h_scaled_size) / cinfo.min_DCT_h_scaled_size;
                int v_in_group = (componentInfo.V_samp_factor * componentInfo.DCT_v_scaled_size) / cinfo.min_DCT_v_scaled_size;
                int h_out_group = cinfo.m_max_h_samp_factor;
                int v_out_group = cinfo.m_max_v_samp_factor;

                /* save for use later */
                m_rowgroup_height[ci] = v_in_group;

                if (!componentInfo.component_needed)
                {
                    /* Don't bother to upsample an uninteresting component. */
                    m_upsampleMethods[ci] = ComponentUpsampler.noop_upsampler;
                    continue;		/* don't need to allocate buffer */
                }

                if (h_in_group == h_out_group && v_in_group == v_out_group)
                {
                    /* Fullsize components can be processed without any work. */
                    m_upsampleMethods[ci] = ComponentUpsampler.fullsize_upsampler;
                    continue;		/* don't need to allocate buffer */
                }

                if (h_in_group * 2 == h_out_group && v_in_group == v_out_group)
                {
                    /* Special cases for 2h1v upsampling */
                    if (do_fancy && componentInfo.downsampled_width > 2)
                        m_upsampleMethods[ci] = ComponentUpsampler.h2v1_fancy_upsampler;
                    else
                        m_upsampleMethods[ci] = ComponentUpsampler.h2v1_upsampler;
                }
                else if (h_in_group == h_out_group && v_in_group * 2 == v_out_group && do_fancy)
                {
                    /* Non-fancy 1h2v upsampling is handled by the generic method */
                    m_upsampleMethods[ci] = ComponentUpsampler.h1v2_fancy_upsampler;
                    m_need_context_rows = true;
                }
                else if (h_in_group * 2 == h_out_group && v_in_group * 2 == v_out_group)
                {
                    /* Special cases for 2h2v upsampling */
                    if (do_fancy && componentInfo.downsampled_width > 2)
                    {
                        m_upsampleMethods[ci] = ComponentUpsampler.h2v2_fancy_upsampler;
                        m_need_context_rows = true;
                    }
                    else
                    {
                        m_upsampleMethods[ci] = ComponentUpsampler.h2v2_upsampler;
                    }
                }
                else if ((h_out_group % h_in_group) == 0 && (v_out_group % v_in_group) == 0)
                {
                    /* Generic integral-factors upsampling method */
                    m_upsampleMethods[ci] = ComponentUpsampler.int_upsampler;
                    m_h_expand[ci] = (byte)(h_out_group / h_in_group);
                    m_v_expand[ci] = (byte)(v_out_group / v_in_group);
                }
                else
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_FRACT_SAMPLE_NOTIMPL);
                }

                ComponentBuffer cb = new ComponentBuffer();
                cb.SetBuffer(jpeg_common_struct.AllocJpegSamples(
                    JpegUtils.jround_up(cinfo.m_output_width, 
                    cinfo.m_max_h_samp_factor), cinfo.m_max_v_samp_factor));

                m_color_buf[ci] = cb;
            }
        }

        /// <summary>
        /// Initialize for an upsampling pass.
        /// </summary>
        public override void start_pass()
        {
            /* Mark the conversion buffer empty */
            m_next_row_out = m_cinfo.m_max_v_samp_factor;

            /* Initialize total-height counter for detecting bottom of image */
            m_rows_to_go = m_cinfo.m_output_height;
        }

        /// <summary>
        /// Control routine to do upsampling (and color conversion).
        /// 
        /// In this version we upsample each component independently.
        /// We upsample one row group into the conversion buffer, then apply
        /// color conversion a row at a time.
        /// </summary>
        public override void upsample(ComponentBuffer[] input_buf, ref int in_row_group_ctr, int in_row_groups_avail, byte[][] output_buf, ref int out_row_ctr, int out_rows_avail)
        {
            /* Fill the conversion buffer, if it's empty */
            if (m_next_row_out >= m_cinfo.m_max_v_samp_factor)
            {
                for (int ci = 0; ci < m_cinfo.m_num_components; ci++)
                {
                    m_perComponentOffsets[ci] = 0;

                    /* Invoke per-component upsample method.*/
                    m_currentComponent = ci;
                    m_upsampleRowOffset = in_row_group_ctr * m_rowgroup_height[ci];
                    upsampleComponent(input_buf[ci]);
                }

                m_next_row_out = 0;
            }

            /* Color-convert and emit rows */

            /* How many we have in the buffer: */
            int num_rows = m_cinfo.m_max_v_samp_factor - m_next_row_out;

            /* Not more than the distance to the end of the image.  Need this test
             * in case the image height is not a multiple of max_v_samp_factor:
             */
            if (num_rows > m_rows_to_go)
                num_rows = m_rows_to_go;

            /* And not more than what the client can accept: */
            out_rows_avail -= out_row_ctr;
            if (num_rows > out_rows_avail)
                num_rows = out_rows_avail;

            m_cinfo.m_cconvert.color_convert(m_color_buf, m_perComponentOffsets, m_next_row_out, output_buf, out_row_ctr, num_rows);

            /* Adjust counts */
            out_row_ctr += num_rows;
            m_rows_to_go -= num_rows;
            m_next_row_out += num_rows;

            /* When the buffer is emptied, declare this input row group consumed */
            if (m_next_row_out >= m_cinfo.m_max_v_samp_factor)
                in_row_group_ctr++;
        }

        private void upsampleComponent(ComponentBuffer input_data)
        {
            switch (m_upsampleMethods[m_currentComponent])
            {
                case ComponentUpsampler.noop_upsampler:
                    noop_upsample();
                    break;
                case ComponentUpsampler.fullsize_upsampler:
                    fullsize_upsample(input_data);
                    break;
                case ComponentUpsampler.h2v1_upsampler:
                    h2v1_upsample(input_data);
                    break;
                case ComponentUpsampler.h2v2_upsampler:
                    h2v2_upsample(input_data);
                    break;
                case ComponentUpsampler.int_upsampler:
                    int_upsample(input_data);
                    break;
                case ComponentUpsampler.h2v1_fancy_upsampler:
                    h2v1_fancy_upsample(input_data);
                    break;
                case ComponentUpsampler.h1v2_fancy_upsampler:
                    h1v2_fancy_upsample(input_data);
                    break;
                case ComponentUpsampler.h2v2_fancy_upsampler:
                    h2v2_fancy_upsample(input_data);
                    break;
                default:
                    m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
                    break;
            }
        }

        /*
         * These are the routines invoked to upsample pixel values
         * of a single component.  One row group is processed per call.
         */

        /// <summary>
        /// This is a no-op version used for "uninteresting" components.
        /// These components will not be referenced by color conversion.
        /// </summary>
        private static void noop_upsample()
        {
            // do nothing
        }

        /// <summary>
        /// For full-size components, we just make color_buf[ci] point at the
        /// input buffer, and thus avoid copying any data.  Note that this is
        /// safe only because sep_upsample doesn't declare the input row group
        /// "consumed" until we are done color converting and emitting it.
        /// </summary>
        private void fullsize_upsample(ComponentBuffer input_data)
        {
            m_color_buf[m_currentComponent] = input_data;
            m_perComponentOffsets[m_currentComponent] = m_upsampleRowOffset;
        }


        /// <summary>
        /// Fast processing for the common case of 2:1 horizontal and 1:1 vertical.
        /// It's still a box filter.
        /// </summary>
        private void h2v1_upsample(ComponentBuffer input_data)
        {
            ComponentBuffer output_data = m_color_buf[m_currentComponent];

            for (int inrow = 0; inrow < m_cinfo.m_max_v_samp_factor; inrow++)
            {
                int row = m_upsampleRowOffset + inrow;
                int outIndex = 0;

                var inputBuffer = input_data[row];
                var outputBuffer = output_data[inrow];
                for (int col = 0; outIndex < m_cinfo.m_output_width; col++)
                {
                    byte invalue = inputBuffer[col]; /* don't need GETJSAMPLE() here */
                    outputBuffer[outIndex++] = invalue;
                    outputBuffer[outIndex++] = invalue;
                }
            }
        }

        /// <summary>
        /// Fast processing for the common case of 2:1 horizontal and 2:1 vertical.
        /// It's still a box filter.
        /// </summary>
        private void h2v2_upsample(ComponentBuffer input_data)
        {
            ComponentBuffer output_data = m_color_buf[m_currentComponent];

            int inrow = 0;
            int outrow = 0;
            while (outrow < m_cinfo.m_max_v_samp_factor)
            {
                int row = m_upsampleRowOffset + inrow;
                int outIndex = 0;

                var inputBuffer = input_data[row];
                var outputBuffer = output_data[outrow];
                for (int col = 0; outIndex < m_cinfo.m_output_width; col++)
                {
                    byte invalue = inputBuffer[col]; /* don't need GETJSAMPLE() here */
                    outputBuffer[outIndex++] = invalue;
                    outputBuffer[outIndex++] = invalue;
                }

                JpegUtils.jcopy_sample_rows(output_data, outrow, output_data, outrow + 1, 1, m_cinfo.m_output_width);
                inrow++;
                outrow += 2;
            }
        }

        /*
         * Fancy upsampling, ported from libjpeg-turbo's jdsample.c.  The centers
         * of the output pixels are 1/4 and 3/4 of the way between input pixel
         * centers ("triangle filter").  The rounding biases alternate so that the
         * results are not systematically biased.
         */

        /// <summary>
        /// Fancy processing for the common case of 2:1 horizontal and 1:1 vertical.
        /// </summary>
        private void h2v1_fancy_upsample(ComponentBuffer input_data)
        {
            ComponentBuffer output_data = m_color_buf[m_currentComponent];
            int width = m_cinfo.Comp_info[m_currentComponent].downsampled_width;

            for (int inrow = 0; inrow < m_cinfo.m_max_v_samp_factor; inrow++)
            {
                byte[] inptr = input_data[m_upsampleRowOffset + inrow];
                byte[] outptr = output_data[inrow];
                int i = 0, o = 0;

                /* Special case for first column */
                int invalue = inptr[i++];
                outptr[o++] = (byte)invalue;
                outptr[o++] = (byte)((invalue * 3 + inptr[i] + 2) >> 2);

                for (int colctr = width - 2; colctr > 0; colctr--)
                {
                    /* General case: 3/4 * nearer pixel + 1/4 * further pixel */
                    invalue = inptr[i++] * 3;
                    outptr[o++] = (byte)((invalue + inptr[i - 2] + 1) >> 2);
                    outptr[o++] = (byte)((invalue + inptr[i] + 2) >> 2);
                }

                /* Special case for last column */
                invalue = inptr[i];
                outptr[o++] = (byte)((invalue * 3 + inptr[i - 1] + 1) >> 2);
                outptr[o] = (byte)invalue;
            }
        }

        /// <summary>
        /// Fancy processing for 1:1 horizontal and 2:1 vertical (4:4:0 subsampling).
        /// </summary>
        private void h1v2_fancy_upsample(ComponentBuffer input_data)
        {
            ComponentBuffer output_data = m_color_buf[m_currentComponent];
            int width = m_cinfo.Comp_info[m_currentComponent].downsampled_width;

            int inrow = 0;
            int outrow = 0;
            while (outrow < m_cinfo.m_max_v_samp_factor)
            {
                for (int v = 0; v < 2; v++)
                {
                    /* inptr0 points to nearest input row, inptr1 points to next nearest */
                    byte[] inptr0 = input_data[m_upsampleRowOffset + inrow];
                    byte[] inptr1;
                    int bias;
                    if (v == 0)
                    {
                        /* next nearest is row above */
                        inptr1 = input_data[m_upsampleRowOffset + inrow - 1];
                        bias = 1;
                    }
                    else
                    {
                        /* next nearest is row below */
                        inptr1 = input_data[m_upsampleRowOffset + inrow + 1];
                        bias = 2;
                    }

                    byte[] outptr = output_data[outrow++];
                    for (int col = 0; col < width; col++)
                    {
                        int thiscolsum = inptr0[col] * 3 + inptr1[col];
                        outptr[col] = (byte)((thiscolsum + bias) >> 2);
                    }
                }

                inrow++;
            }
        }

        /// <summary>
        /// Fancy processing for the common case of 2:1 horizontal and 2:1 vertical.
        /// </summary>
        private void h2v2_fancy_upsample(ComponentBuffer input_data)
        {
            ComponentBuffer output_data = m_color_buf[m_currentComponent];
            int width = m_cinfo.Comp_info[m_currentComponent].downsampled_width;

            int inrow = 0;
            int outrow = 0;
            while (outrow < m_cinfo.m_max_v_samp_factor)
            {
                for (int v = 0; v < 2; v++)
                {
                    /* inptr0 points to nearest input row, inptr1 points to next nearest */
                    byte[] inptr0 = input_data[m_upsampleRowOffset + inrow];
                    byte[] inptr1 = v == 0
                        ? input_data[m_upsampleRowOffset + inrow - 1]   /* next nearest is row above */
                        : input_data[m_upsampleRowOffset + inrow + 1];  /* next nearest is row below */
                    byte[] outptr = output_data[outrow++];
                    H2v2FancyRow(inptr0, inptr1, outptr, width);
                }

                inrow++;
            }
        }

        /// <summary>
        /// One output row of the 2h2v fancy (triangle) upsampler.
        /// colsum[i] = in0[i]*3 + in1[i]; out[2i] = (colsum[i]*3 + colsum[i-1] + 8) >> 4;
        /// out[2i+1] = (colsum[i]*3 + colsum[i+1] + 7) >> 4; the first and last columns
        /// are special cases. Requires width >= 2.
        /// </summary>
        internal static void H2v2FancyRow(byte[] in0, byte[] in1, byte[] outp, int width)
        {
            int i = 1;
            if (Vector128.IsHardwareAccelerated && width >= 18)
            {
                /* Special case for first column */
                int c0 = in0[0] * 3 + in1[0];
                int c1 = in0[1] * 3 + in1[1];
                outp[0] = (byte)((c0 * 4 + 8) >> 4);
                outp[1] = (byte)((c0 * 3 + c1 + 7) >> 4);

                /* 16 input columns per step; the loads at i-1 .. i+16 stay inside the row */
                Vector128<ushort> three = Vector128.Create((ushort)3);
                Vector128<ushort> eight = Vector128.Create((ushort)8);
                Vector128<ushort> seven = Vector128.Create((ushort)7);
                for (; i + 16 <= width - 1; i += 16)
                {
                    Vector128<byte> a = Vector128.LoadUnsafe(ref in0[i - 1]);
                    Vector128<byte> b = Vector128.LoadUnsafe(ref in1[i - 1]);
                    Vector128<byte> c = Vector128.LoadUnsafe(ref in0[i]);
                    Vector128<byte> d = Vector128.LoadUnsafe(ref in1[i]);
                    Vector128<byte> e = Vector128.LoadUnsafe(ref in0[i + 1]);
                    Vector128<byte> f = Vector128.LoadUnsafe(ref in1[i + 1]);

                    Vector128<ushort> prevLo = Vector128.WidenLower(a) * three + Vector128.WidenLower(b);
                    Vector128<ushort> prevHi = Vector128.WidenUpper(a) * three + Vector128.WidenUpper(b);
                    Vector128<ushort> curLo = Vector128.WidenLower(c) * three + Vector128.WidenLower(d);
                    Vector128<ushort> curHi = Vector128.WidenUpper(c) * three + Vector128.WidenUpper(d);
                    Vector128<ushort> nextLo = Vector128.WidenLower(e) * three + Vector128.WidenLower(f);
                    Vector128<ushort> nextHi = Vector128.WidenUpper(e) * three + Vector128.WidenUpper(f);

                    Vector128<ushort> evenLo = Vector128.ShiftRightLogical(curLo * three + prevLo + eight, 4);
                    Vector128<ushort> oddLo = Vector128.ShiftRightLogical(curLo * three + nextLo + seven, 4);
                    Vector128<ushort> evenHi = Vector128.ShiftRightLogical(curHi * three + prevHi + eight, 4);
                    Vector128<ushort> oddHi = Vector128.ShiftRightLogical(curHi * three + nextHi + seven, 4);

                    /* even | odd << 8 as little-endian bytes is the interleaved output */
                    (evenLo | Vector128.ShiftLeft(oddLo, 8)).AsByte().StoreUnsafe(ref outp[2 * i]);
                    (evenHi | Vector128.ShiftLeft(oddHi, 8)).AsByte().StoreUnsafe(ref outp[2 * i + 16]);
                }
            }
            else
            {
                /* Special case for first column */
                int c0 = in0[0] * 3 + in1[0];
                int c1 = in0[1] * 3 + in1[1];
                outp[0] = (byte)((c0 * 4 + 8) >> 4);
                outp[1] = (byte)((c0 * 3 + c1 + 7) >> 4);
            }

            /* Scalar remainder: general case, 9/16, 3/16, 3/16, 1/16 overall */
            int lastcolsum = in0[i - 1] * 3 + in1[i - 1];
            int thiscolsum = in0[i] * 3 + in1[i];
            int o = 2 * i;
            for (; i < width - 1; i++)
            {
                int nextcolsum = in0[i + 1] * 3 + in1[i + 1];
                outp[o++] = (byte)((thiscolsum * 3 + lastcolsum + 8) >> 4);
                outp[o++] = (byte)((thiscolsum * 3 + nextcolsum + 7) >> 4);
                lastcolsum = thiscolsum;
                thiscolsum = nextcolsum;
            }

            /* Special case for last column */
            outp[o++] = (byte)((thiscolsum * 3 + lastcolsum + 8) >> 4);
            outp[o] = (byte)((thiscolsum * 4 + 7) >> 4);
        }

        /// <summary>
        /// This version handles any integral sampling ratios.
        /// This is not used for typical JPEG files, so it need not be fast.
        /// Nor, for that matter, is it particularly accurate: the algorithm is
        /// simple replication of the input pixel onto the corresponding output
        /// pixels.  The hi-falutin sampling literature refers to this as a
        /// "box filter".  A box filter tends to introduce visible artifacts,
        /// so if you are actually going to use 3:1 or 4:1 sampling ratios
        /// you would be well advised to improve this code.
        /// </summary>
        private void int_upsample(ComponentBuffer input_data)
        {
            ComponentBuffer output_data = m_color_buf[m_currentComponent];
            int h_expand = m_h_expand[m_currentComponent];
            int v_expand = m_v_expand[m_currentComponent];

            int inrow = 0;
            int outrow = 0;
            while (outrow < m_cinfo.m_max_v_samp_factor)
            {
                /* Generate one output row with proper horizontal expansion */
                int row = m_upsampleRowOffset + inrow;
                var inputBuffer = input_data[row];
                var outputBuffer = output_data[outrow];
                int outIndex = 0;
                for (int col = 0; outIndex < m_cinfo.m_output_width; col++)
                {
                    byte invalue = inputBuffer[col]; /* don't need GETJSAMPLE() here */
                    for (int h = h_expand; h > 0; h--)
                        outputBuffer[outIndex++] = invalue;
                }
                
                /* Generate any additional output rows by duplicating the first one */
                if (v_expand > 1)
                {
                    JpegUtils.jcopy_sample_rows(output_data, outrow, output_data, 
                        outrow + 1, v_expand - 1, m_cinfo.m_output_width);
                }

                inrow++;
                outrow += v_expand;
            }
        }
    }
}
