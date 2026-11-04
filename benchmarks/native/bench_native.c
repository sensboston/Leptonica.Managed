/*
 * bench_native.c
 *
 * Fixed-operation performance benchmark for the native Leptonica library,
 * validated against the 1.87.0 headers (allheaders.h).
 *
 * Every case name, input and parameter is fixed and matches the managed benchmark
 * so the numbers can be compared with Leptonica.Managed (benchmarks/Leptonica.Managed.Benchmarks).
 *
 * Build: see CMakeLists.txt.  C99, no OpenMP, no global state
 * except the loaded inputs held in `g`.
 */

#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#ifndef _WIN32
/* clock_gettime(), localtime_r(): needed with strict -std=c99 on glibc */
#define _POSIX_C_SOURCE 200809L
#endif

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#ifdef _WIN32
#include <windows.h>
#else
#include <sys/utsname.h>
#endif

#include "allheaders.h"

/* ------------------------------------------------------------------ */
/* Clock, host and provenance                                          */
/* ------------------------------------------------------------------ */

static double now_ms(void)
{
#ifdef _WIN32
    static LARGE_INTEGER freq;   /* constant on Windows; queried once */
    LARGE_INTEGER cnt;

    if (freq.QuadPart == 0)
        QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&cnt);
    return 1000.0 * (double)cnt.QuadPart / (double)freq.QuadPart;
#else
    struct timespec ts;

    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (double)ts.tv_sec * 1000.0 + (double)ts.tv_nsec / 1000000.0;
#endif
}

static void get_cpu_name(char *buf, size_t n)
{
    buf[0] = '\0';
#ifdef _WIN32
    {
        HKEY hkey;
        DWORD size = (DWORD)n;
        DWORD type = 0;

        if (RegOpenKeyExA(HKEY_LOCAL_MACHINE,
                          "HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0",
                          0, KEY_READ, &hkey) == ERROR_SUCCESS) {
            if (RegQueryValueExA(hkey, "ProcessorNameString", NULL, &type,
                                 (LPBYTE)buf, &size) != ERROR_SUCCESS)
                buf[0] = '\0';
            RegCloseKey(hkey);
        }
    }
#else
    {
        FILE *f = fopen("/proc/cpuinfo", "r");
        if (f) {
            char line[512];
            while (fgets(line, (int)sizeof(line), f)) {
                if (strncmp(line, "model name", 10) == 0) {
                    char *p = strchr(line, ':');
                    if (p) {
                        size_t len;
                        p += 2;
                        len = strcspn(p, "\r\n");
                        if (len >= n) len = n - 1;
                        memcpy(buf, p, len);
                        buf[len] = '\0';
                    }
                    break;
                }
            }
            fclose(f);
        }
    }
#endif
    if (buf[0] == '\0') {
        size_t len = strlen("unknown");
        if (len < n) memcpy(buf, "unknown", len + 1);
    }
}

static void get_os_name(char *buf, size_t n)
{
#ifdef _WIN32
    /* Without a supportedOS manifest, GetVersionEx lies; use the registry. */
    HKEY hkey;
    DWORD maj = 0, min = 0;
    char build[32] = "?";
    char product[128] = "Windows";
    DWORD size;

    if (RegOpenKeyExA(HKEY_LOCAL_MACHINE,
                      "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
                      0, KEY_READ, &hkey) == ERROR_SUCCESS) {
        size = sizeof(maj);
        RegQueryValueExA(hkey, "CurrentMajorVersionNumber", NULL, NULL,
                         (LPBYTE)&maj, &size);
        size = sizeof(min);
        RegQueryValueExA(hkey, "CurrentMinorVersionNumber", NULL, NULL,
                         (LPBYTE)&min, &size);
        size = sizeof(build);
        RegQueryValueExA(hkey, "CurrentBuildNumber", NULL, NULL,
                         (LPBYTE)build, &size);
        size = sizeof(product);
        RegQueryValueExA(hkey, "ProductName", NULL, NULL,
                         (LPBYTE)product, &size);
        RegCloseKey(hkey);
    }
    snprintf(buf, n, "%s %lu.%lu (build %s)", product,
             (unsigned long)maj, (unsigned long)min, build);
#else
    struct utsname u;
    if (uname(&u) == 0)
        snprintf(buf, n, "%s %s %s", u.sysname, u.release, u.machine);
    else
        snprintf(buf, n, "unknown");
#endif
}

static const char *compiler_id(void)
{
#ifdef _MSC_VER
    static char buf[32];
    snprintf(buf, sizeof(buf), "MSVC %d", (int)_MSC_VER);
    return buf;
#elif defined(__VERSION__)
    return __VERSION__;
#else
    return "unknown";
#endif
}

static void get_date(char *buf, size_t n)
{
    time_t t = time(NULL);
    struct tm tmv;
#ifdef _WIN32
    localtime_s(&tmv, &t);
#else
    localtime_r(&t, &tmv);
#endif
    strftime(buf, n, "%Y-%m-%d %H:%M:%S", &tmv);
}

/* ------------------------------------------------------------------ */
/* Loaded inputs (the only global state)                               */
/* ------------------------------------------------------------------ */

typedef struct {
    PIX        *feyn;           /* feyn.tif, 1 bpp, 2528x3300           */
    PIX        *feyn_t3;        /* feyn translated by (3,3)             */
    PIX        *feyn_ropdst;    /* dest copy reused by rop_src          */
    PIX        *feyn_xordst;    /* dest copy reused by rop_xor_shift    */
    PIX        *gray8;          /* pixConvertTo8(test8.jpg, 0)          */
    PIX        *gray_page;      /* 2x pixScaleGrayLI of marge luminance */
    PIX        *gray_lucasta;   /* pixConvertTo8(lucasta.150.jpg, 0)    */
    PIX        *marge;          /* marge.jpg, 32 bpp                    */
    PIX        *test24;         /* test24.jpg, 32 bpp                   */
    PIX        *cmapped;        /* weasel-64g.png, 8 bpp + colormap     */
    PIX        *seed_bin;       /* 1 bpp, a few pixels set              */
    PIX        *seed_gray;      /* 8 bpp, a few pixels set              */
    PIX        *mask_gray;      /* 8 bpp mask for seedfill_gray         */
    SEL        *sel_hmt;        /* 3x3 corner sel for pixHMT            */
    L_KERNEL   *kel_gauss;      /* gaussian kernel for pixConvolve      */
    PTA        *ptas, *ptad;    /* fixed affine point pairs             */
    l_uint8    *png32;          /* PNG of marge, encoded at setup       */
    size_t      png32_size;
    l_uint8    *tiffg4;         /* G4 TIFF of feyn, encoded at setup    */
    size_t      tiffg4_size;
    l_uint8    *jpgbytes;       /* raw bytes of marge.jpg               */
    size_t      jpgbytes_size;
    int         fail;           /* set by a run() when a call failed    */
} BenchCtx;

static BenchCtx g;

/* ------------------------------------------------------------------ */
/* One function per case.  Every result is destroyed inside the loop   */
/* exactly the way a library user would.  On failure: set c->fail.     */
/* ------------------------------------------------------------------ */

static void run_rop_src(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    if (pixRasterop(c->feyn_ropdst, 100, 100, 2000, 2000,
                    PIX_SRC, c->feyn, 0, 0) != 0)
        c->fail = 1;
}

static void run_rop_xor_shift(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    if (pixRasterop(c->feyn_xordst, 1, 1,
                    pixGetWidth(c->feyn) - 2, pixGetHeight(c->feyn) - 2,
                    PIX_SRC ^ PIX_DST, c->feyn, 0, 0) != 0)
        c->fail = 1;
}

static void run_invert_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixInvert(NULL, c->feyn);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_and_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixAnd(NULL, c->feyn, c->feyn_t3);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_dilate_brick_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDilateBrick(NULL, c->feyn, 5, 5);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_erode_brick_15x1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixErodeBrick(NULL, c->feyn, 15, 1);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_open_brick_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixOpenBrick(NULL, c->feyn, 5, 5);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_close_brick_21(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixCloseBrick(NULL, c->feyn, 21, 21);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_dilate_comp_21(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDilateCompBrick(NULL, c->feyn, 21, 21);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_dilate_dwa_21(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDilateBrickDwa(NULL, c->feyn, 21, 21);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_close_comp_dwa_51(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixCloseCompBrickDwa(NULL, c->feyn, 51, 51);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_morphseq(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixMorphSequence(c->feyn, "o5.5 + c21.1 + d3.3", 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_hmt_sel(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixHMT(NULL, c->feyn, c->sel_hmt);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_dilate_gray_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDilateGray(c->gray_page, 5, 5);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_erode_gray_15(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixErodeGray(c->gray_page, 15, 15);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_tophat_white_15(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixTophat(c->gray_page, 15, 15, L_TOPHAT_WHITE);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_1_0_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScale(c->feyn, 0.5f, 0.5f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_8_0_37(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScale(c->gray8, 0.37f, 0.37f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_8_2_3(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScale(c->gray8, 2.3f, 2.3f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_32_0_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScale(c->marge, 0.5f, 0.5f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_32_1_6(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScale(c->marge, 1.6f, 1.6f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_li_8_2x(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScaleGrayLI(c->gray8, 2.0f, 2.0f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_area_8_0_3(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScaleAreaMap(c->gray8, 0.3f, 0.3f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_to_gray_4(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScaleToGray4(c->feyn);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_scale_sampling(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixScaleBySampling(c->marge, 0.71f, 0.71f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_rgb_to_gray(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixConvertRGBToGray(c->marge, 0.3f, 0.5f, 0.2f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_rgb_to_lum(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixConvertRGBToLuminance(c->marge);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_conv_1_to_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixConvert1To8(NULL, c->feyn, 255, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_conv_8_to_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixConvert8To32(c->gray8);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_remove_cmap(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRemoveColormap(c->cmapped, REMOVE_CMAP_BASED_ON_SRC);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_thresh_8_to_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixThresholdToBinary(c->gray8, 128);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_dither_8_to_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDitherToBinary(c->gray8);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_otsu_adaptive(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixth = NULL;   /* thresholded image (unused, destroyed)     */
    PIX *pixd = NULL;    /* min-box pix (result of the timed call)    */

    if (pixOtsuAdaptiveThreshold(c->gray8, 200, 200, 0, 0, 0.1f,
                                 &pixth, &pixd) != 0 || !pixd) {
        c->fail = 1;
        pixDestroy(&pixth);
        pixDestroy(&pixd);
        return;
    }
    pixDestroy(&pixth);
    pixDestroy(&pixd);
}

static void run_octree_quant(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixOctreeColorQuant(c->marge, 128, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_median_cut(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixMedianCutQuant(c->marge, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_blockconv_8_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixBlockconv(c->gray8, 5, 5);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_blockconv_32_5(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixBlockconv(c->marge, 5, 5);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_convolve_kernel(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixConvolve(c->gray8, c->kel_gauss, 8, 1);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_unsharp(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixUnsharpMasking(c->marge, 3, 0.5f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_bilateral_fast(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixBilateral(c->gray8, 5.0f, 10.0f, 10, 1);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_median_filter_3(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixMedianFilter(c->gray8, 3, 3);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_edge_sobel(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixSobelEdgeFilter(c->gray8, L_ALL_EDGES);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_bg_norm(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixBackgroundNormSimple(c->marge, NULL, NULL);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_seedfill_binary(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixSeedfillBinary(NULL, c->seed_bin, c->feyn, 8);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_conncomp_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    BOXA *boxa = pixConnComp(c->feyn, NULL, 8);
    if (!boxa) { c->fail = 1; return; }
    boxaDestroy(&boxa);
}

static void run_conncomp_pixa(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIXA *pixa = NULL;
    BOXA *boxa = pixConnComp(c->feyn, &pixa, 8);
    if (!boxa) { c->fail = 1; return; }
    boxaDestroy(&boxa);
    pixaDestroy(&pixa);
}

static void run_count_conncomp(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_int32 n = 0;
    if (pixCountConnComp(c->feyn, 8, &n) != 0)
        c->fail = 1;
}

static void run_distance_fn(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDistanceFunction(c->feyn, 8, 8, L_BOUNDARY_BG);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_seedfill_gray(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    /* The per-run copy of the seed is part of this case (see README). */
    PIX *pixd = pixCopy(NULL, c->seed_gray);
    if (!pixd) { c->fail = 1; return; }
    if (pixSeedfillGray(pixd, c->mask_gray, 4) != 0)
        c->fail = 1;
    pixDestroy(&pixd);
}

static void run_rotate_shear_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRotate(c->feyn, 0.05f, L_ROTATE_SHEAR,
                          L_BRING_IN_WHITE, 0, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_rotate_area_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRotate(c->gray8, 0.05f, L_ROTATE_AREA_MAP,
                          L_BRING_IN_WHITE, 0, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_rotate_area_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRotate(c->marge, 0.05f, L_ROTATE_AREA_MAP,
                          L_BRING_IN_WHITE, 0, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_rotate_90_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRotate90(c->feyn, 1);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_rotate_90_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRotate90(c->marge, 1);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_flip_lr_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixFlipLR(NULL, c->gray8);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_find_skew(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_float32 angle = 0.0f, conf = 0.0f;
    if (pixFindSkew(c->feyn, &angle, &conf) != 0)
        c->fail = 1;
}

static void run_deskew(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixDeskew(c->feyn, 0);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_affine_sampled(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    /* 1.87.0 signature: pixAffineSampledPta(pixs, ptad, ptas, incolor) */
    PIX *pixd = pixAffineSampledPta(c->gray8, c->ptad, c->ptas,
                                    L_BRING_IN_WHITE);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_histogram_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    /* 1.87.0 returns a NUMA*, not a PIXA*. */
    NUMA *na = pixGetGrayHistogram(c->gray8, 1);
    if (!na) { c->fail = 1; return; }
    numaDestroy(&na);
}

static void run_count_pixels_1(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_int32 count = 0;
    if (pixCountPixels(c->feyn, &count, NULL) != 0)
        c->fail = 1;
}

static void run_rank_filter_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixRankFilterGray(c->gray8, 5, 5, 0.5f);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_png_encode_8(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_uint8 *data = NULL;
    size_t size = 0;
    if (pixWriteMem(&data, &size, c->gray8, IFF_PNG) != 0) {
        c->fail = 1;
        return;
    }
    lept_free(data);
}

static void run_png_encode_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_uint8 *data = NULL;
    size_t size = 0;
    if (pixWriteMem(&data, &size, c->marge, IFF_PNG) != 0) {
        c->fail = 1;
        return;
    }
    lept_free(data);
}

static void run_png_decode_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixReadMem(c->png32, c->png32_size);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_jpeg_decode_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixReadMem(c->jpgbytes, c->jpgbytes_size);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

static void run_jpeg_encode_32(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_uint8 *data = NULL;
    size_t size = 0;
    if (pixWriteMem(&data, &size, c->marge, IFF_JFIF_JPEG) != 0) {
        c->fail = 1;
        return;
    }
    lept_free(data);
}

static void run_tiff_g4_encode(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    l_uint8 *data = NULL;
    size_t size = 0;
    if (pixWriteMem(&data, &size, c->feyn, IFF_TIFF_G4) != 0) {
        c->fail = 1;
        return;
    }
    lept_free(data);
}

static void run_tiff_g4_decode(void *vctx)
{
    BenchCtx *c = (BenchCtx *)vctx;
    PIX *pixd = pixReadMem(c->tiffg4, c->tiffg4_size);
    if (!pixd) { c->fail = 1; return; }
    pixDestroy(&pixd);
}

/* ------------------------------------------------------------------ */
/* The fixed case table (grouped in the documented order)              */
/* ------------------------------------------------------------------ */

typedef struct {
    const char *name;       /* case name, unique, matches the port     */
    const char *function;   /* leptonica entry point                   */
    const char *input;      /* short input label                       */
    const char *params;     /* exact parameters                        */
    void (*run)(void *ctx); /* one measured call + destruction         */
    PIX **pixpp;            /* primary image, for w/h/d reporting      */
} BenchCase;

static const BenchCase cases[] = {
    /* Rasterop / logical (1 bpp feyn) */
    { "rop_src", "pixRasterop", "feyn",
      "dx=100, dy=100, w=2000, h=2000, op=PIX_SRC, sx=0, sy=0",
      run_rop_src, &g.feyn },
    { "rop_xor_shift", "pixRasterop", "feyn",
      "dx=1, dy=1, w=W-2, h=H-2, op=PIX_SRC^PIX_DST, sx=0, sy=0",
      run_rop_xor_shift, &g.feyn },
    { "invert_1", "pixInvert", "feyn", "",
      run_invert_1, &g.feyn },
    { "and_1", "pixAnd", "feyn+feyn_t3",
      "pixs2=feyn translated by (3,3), incolor=L_BRING_IN_WHITE",
      run_and_1, &g.feyn },

    /* Binary morphology (1 bpp feyn) */
    { "dilate_brick_5", "pixDilateBrick", "feyn", "hsize=5, vsize=5",
      run_dilate_brick_5, &g.feyn },
    { "erode_brick_15x1", "pixErodeBrick", "feyn", "hsize=15, vsize=1",
      run_erode_brick_15x1, &g.feyn },
    { "open_brick_5", "pixOpenBrick", "feyn", "hsize=5, vsize=5",
      run_open_brick_5, &g.feyn },
    { "close_brick_21", "pixCloseBrick", "feyn", "hsize=21, vsize=21",
      run_close_brick_21, &g.feyn },
    { "dilate_comp_21", "pixDilateCompBrick", "feyn", "hsize=21, vsize=21",
      run_dilate_comp_21, &g.feyn },
    { "dilate_dwa_21", "pixDilateBrickDwa", "feyn", "hsize=21, vsize=21",
      run_dilate_dwa_21, &g.feyn },
    { "close_comp_dwa_51", "pixCloseCompBrickDwa", "feyn", "hsize=51, vsize=51",
      run_close_comp_dwa_51, &g.feyn },
    { "morphseq", "pixMorphSequence", "feyn",
      "sequence=\"o5.5 + c21.1 + d3.3\", display=0",
      run_morphseq, &g.feyn },
    { "hmt_sel", "pixHMT", "feyn",
      "sel=selCreateFromString(\"xxxxXxxoo\", 3, 3, \"corner3x3\") "
      "(rows xxx/xXx/xoo, X = origin at center, no separators)",
      run_hmt_sel, &g.feyn },

    /* Gray morphology (8 bpp page) */
    { "dilate_gray_5", "pixDilateGray", "gray_page", "hsize=5, vsize=5",
      run_dilate_gray_5, &g.gray_page },
    { "erode_gray_15", "pixErodeGray", "gray_page", "hsize=15, vsize=15",
      run_erode_gray_15, &g.gray_page },
    { "tophat_white_15", "pixTophat", "gray_page",
      "hsize=15, vsize=15, type=L_TOPHAT_WHITE",
      run_tophat_white_15, &g.gray_page },

    /* Scaling */
    { "scale_1_0.5", "pixScale", "feyn", "scalex=0.5, scaley=0.5",
      run_scale_1_0_5, &g.feyn },
    { "scale_8_0.37", "pixScale", "gray8", "scalex=0.37, scaley=0.37",
      run_scale_8_0_37, &g.gray8 },
    { "scale_8_2.3", "pixScale", "gray8", "scalex=2.3, scaley=2.3",
      run_scale_8_2_3, &g.gray8 },
    { "scale_32_0.5", "pixScale", "marge", "scalex=0.5, scaley=0.5",
      run_scale_32_0_5, &g.marge },
    { "scale_32_1.6", "pixScale", "marge", "scalex=1.6, scaley=1.6",
      run_scale_32_1_6, &g.marge },
    { "scale_li_8_2x", "pixScaleGrayLI", "gray8", "scalex=2.0, scaley=2.0",
      run_scale_li_8_2x, &g.gray8 },
    { "scale_area_8_0.3", "pixScaleAreaMap", "gray8", "scalex=0.3, scaley=0.3",
      run_scale_area_8_0_3, &g.gray8 },
    { "scale_to_gray_4", "pixScaleToGray4", "feyn", "",
      run_scale_to_gray_4, &g.feyn },
    { "scale_sampling", "pixScaleBySampling", "marge",
      "scalex=0.71, scaley=0.71",
      run_scale_sampling, &g.marge },

    /* Depth conversion */
    { "rgb_to_gray", "pixConvertRGBToGray", "marge",
      "rwt=0.3, gwt=0.5, bwt=0.2",
      run_rgb_to_gray, &g.marge },
    { "rgb_to_lum", "pixConvertRGBToLuminance", "marge", "",
      run_rgb_to_lum, &g.marge },
    { "conv_1_to_8", "pixConvert1To8", "feyn", "val0=255, val1=0",
      run_conv_1_to_8, &g.feyn },
    { "conv_8_to_32", "pixConvert8To32", "gray8", "",
      run_conv_8_to_32, &g.gray8 },
    { "remove_cmap", "pixRemoveColormap", "cmapped",
      "type=REMOVE_CMAP_BASED_ON_SRC",
      run_remove_cmap, &g.cmapped },
    { "thresh_8_to_1", "pixThresholdToBinary", "gray8", "threshold=128",
      run_thresh_8_to_1, &g.gray8 },
    { "dither_8_to_1", "pixDitherToBinary", "gray8", "",
      run_dither_8_to_1, &g.gray8 },
    { "otsu_adaptive", "pixOtsuAdaptiveThreshold", "gray8",
      "sx=200, sy=200, smoothx=0, smoothy=0, scorefract=0.1, "
      "ppixth=&pixth, ppixd=&pixd",
      run_otsu_adaptive, &g.gray8 },
    { "octree_quant", "pixOctreeColorQuant", "marge",
      "colors=128, ditherflag=0",
      run_octree_quant, &g.marge },
    { "median_cut", "pixMedianCutQuant", "marge", "ditherflag=0",
      run_median_cut, &g.marge },

    /* Filtering / convolution */
    { "blockconv_8_5", "pixBlockconv", "gray8", "wc=5, hc=5",
      run_blockconv_8_5, &g.gray8 },
    { "blockconv_32_5", "pixBlockconv", "marge", "wc=5, hc=5",
      run_blockconv_32_5, &g.marge },
    { "convolve_kernel", "pixConvolve", "gray8",
      "kel=makeGaussianKernel(3, 3, 1.0, 1.0), outdepth=8, normflag=1",
      run_convolve_kernel, &g.gray8 },
    { "unsharp", "pixUnsharpMasking", "marge",
      "halfwidth=3, fract=0.5",
      run_unsharp, &g.marge },
    { "bilateral_fast", "pixBilateral", "gray8",
      "spatial_stdev=5.0, range_stdev=10.0, ncomps=10, reduction=1",
      run_bilateral_fast, &g.gray8 },
    { "median_filter_3", "pixMedianFilter", "gray8", "wf=3, hf=3",
      run_median_filter_3, &g.gray8 },
    { "edge_sobel", "pixSobelEdgeFilter", "gray8",
      "orientflag=L_ALL_EDGES",
      run_edge_sobel, &g.gray8 },
    { "bg_norm", "pixBackgroundNormSimple", "marge",
      "pixim=NULL, pixg=NULL",
      run_bg_norm, &g.marge },

    /* Seedfill / connected components / distance */
    { "seedfill_binary", "pixSeedfillBinary", "seed_bin+feyn",
      "pixs=seed_bin (3 pixels), pixm=feyn, connectivity=8",
      run_seedfill_binary, &g.seed_bin },
    { "conncomp_8", "pixConnComp", "feyn",
      "ppixa=NULL, connectivity=8",
      run_conncomp_8, &g.feyn },
    { "conncomp_pixa", "pixConnComp", "feyn",
      "ppixa=&pixa, connectivity=8",
      run_conncomp_pixa, &g.feyn },
    { "count_conncomp", "pixCountConnComp", "feyn",
      "connectivity=8, pcount=&n",
      run_count_conncomp, &g.feyn },
    { "distance_fn", "pixDistanceFunction", "feyn",
      "connectivity=8, outdepth=8, boundcond=L_BOUNDARY_BG",
      run_distance_fn, &g.feyn },
    { "seedfill_gray", "pixSeedfillGray", "seed_gray+mask_gray",
      "connectivity=4; per run: pixCopy(NULL, seed_gray) then in-place fill "
      "(copy is timed)",
      run_seedfill_gray, &g.seed_gray },

    /* Geometry */
    { "rotate_shear_1", "pixRotate", "feyn",
      "angle=0.05, type=L_ROTATE_SHEAR, incolor=L_BRING_IN_WHITE, "
      "width=0, height=0",
      run_rotate_shear_1, &g.feyn },
    { "rotate_area_8", "pixRotate", "gray8",
      "angle=0.05, type=L_ROTATE_AREA_MAP, incolor=L_BRING_IN_WHITE, "
      "width=0, height=0",
      run_rotate_area_8, &g.gray8 },
    { "rotate_area_32", "pixRotate", "marge",
      "angle=0.05, type=L_ROTATE_AREA_MAP, incolor=L_BRING_IN_WHITE, "
      "width=0, height=0",
      run_rotate_area_32, &g.marge },
    { "rotate_90_1", "pixRotate90", "feyn", "direction=1 (clockwise)",
      run_rotate_90_1, &g.feyn },
    { "rotate_90_32", "pixRotate90", "marge", "direction=1 (clockwise)",
      run_rotate_90_32, &g.marge },
    { "flip_lr_8", "pixFlipLR", "gray8", "",
      run_flip_lr_8, &g.gray8 },
    { "find_skew", "pixFindSkew", "feyn", "pangle, pconf",
      run_find_skew, &g.feyn },
    { "deskew", "pixDeskew", "feyn", "redsearch=0",
      run_deskew, &g.feyn },
    { "affine_sampled", "pixAffineSampledPta", "gray8",
      "incolor=L_BRING_IN_WHITE, ptas=[(0,0),(W-1,0),(0,H-1)], "
      "ptad=[(0.02*W,0),(W-1,0.02*W),(0,H-1)]",
      run_affine_sampled, &g.gray8 },

    /* Statistics */
    { "histogram_8", "pixGetGrayHistogram", "gray8", "factor=1",
      run_histogram_8, &g.gray8 },
    { "count_pixels_1", "pixCountPixels", "feyn", "tab8=NULL",
      run_count_pixels_1, &g.feyn },
    { "rank_filter_8", "pixRankFilterGray", "gray8",
      "wf=5, hf=5, rank=0.5",
      run_rank_filter_8, &g.gray8 },

    /* I/O (in memory, no disk) */
    { "png_encode_8", "pixWriteMem", "gray8", "format=IFF_PNG",
      run_png_encode_8, &g.gray8 },
    { "png_encode_32", "pixWriteMem", "marge", "format=IFF_PNG",
      run_png_encode_32, &g.marge },
    { "png_decode_32", "pixReadMem", "png32(marge)",
      "buffer=PNG of marge encoded at setup",
      run_png_decode_32, &g.marge },
    { "jpeg_decode_32", "pixReadMem", "marge.jpg bytes",
      "buffer=raw file bytes of marge.jpg",
      run_jpeg_decode_32, &g.marge },
    { "jpeg_encode_32", "pixWriteMem", "marge",
      "format=IFF_JFIF_JPEG",
      run_jpeg_encode_32, &g.marge },
    { "tiff_g4_encode", "pixWriteMem", "feyn", "format=IFF_TIFF_G4",
      run_tiff_g4_encode, &g.feyn },
    { "tiff_g4_decode", "pixReadMem", "tiffg4(feyn)",
      "buffer=G4 TIFF of feyn encoded at setup",
      run_tiff_g4_decode, &g.feyn }
};

static const int ncases = (int)(sizeof(cases) / sizeof(cases[0]));

/* ------------------------------------------------------------------ */
/* Loading (never timed)                                               */
/* ------------------------------------------------------------------ */

static void fail_hard(const char *what)
{
    fprintf(stderr, "fatal: setup failed: %s\n", what);
    exit(2);
}

static PIX *load_pix(const char *dir, const char *fname, int required)
{
    char path[4096];
    PIX *pix;

    snprintf(path, sizeof(path), "%s/%s", dir, fname);  /* '/' works on Windows */
    pix = pixRead(path);
    if (!pix) {
        if (required) {
            fprintf(stderr, "error: cannot read required input %s\n", path);
            exit(2);
        }
        fprintf(stderr, "warning: cannot read optional input %s\n", path);
    }
    return pix;
}

static l_uint8 *read_file_bytes(const char *dir, const char *fname,
                                size_t *psize)
{
    char path[4096];
    FILE *fp;
    l_uint8 *buf;
    long len;

    *psize = 0;
    snprintf(path, sizeof(path), "%s/%s", dir, fname);
    fp = fopen(path, "rb");
    if (!fp) return NULL;
    if (fseek(fp, 0, SEEK_END) != 0) { fclose(fp); return NULL; }
    len = ftell(fp);
    if (len <= 0) { fclose(fp); return NULL; }
    if (fseek(fp, 0, SEEK_SET) != 0) { fclose(fp); return NULL; }
    buf = (l_uint8 *)malloc((size_t)len);
    if (!buf) { fclose(fp); fail_hard("out of memory"); }
    if (fread(buf, 1, (size_t)len, fp) != (size_t)len) {
        free(buf);
        fclose(fp);
        return NULL;
    }
    fclose(fp);
    *psize = (size_t)len;
    return buf;
}

static void load_inputs(const char *dir)
{
    PIX *pix1;
    l_int32 w, h;

    /* 1 bpp */
    g.feyn = load_pix(dir, "feyn.tif", 1);
    g.feyn_t3 = pixTranslate(NULL, g.feyn, 3, 3, L_BRING_IN_WHITE);
    if (!g.feyn_t3) fail_hard("pixTranslate(feyn, 3, 3, L_BRING_IN_WHITE)");
    g.feyn_ropdst = pixCopy(NULL, g.feyn);
    g.feyn_xordst = pixCopy(NULL, g.feyn);
    if (!g.feyn_ropdst || !g.feyn_xordst) fail_hard("pixCopy(feyn)");

    /* 8 bpp from test8.jpg */
    pix1 = load_pix(dir, "test8.jpg", 1);
    g.gray8 = pixConvertTo8(pix1, 0);   /* 1.87: cmapflag=0, no colormap */
    pixDestroy(&pix1);
    if (!g.gray8) fail_hard("pixConvertTo8(test8.jpg, 0)");

    /* 32 bpp marge.jpg + large 8 bpp page derived from it */
    g.marge = load_pix(dir, "marge.jpg", 1);
    if (pixGetDepth(g.marge) != 32)
        fprintf(stderr, "warning: marge.jpg is %d bpp, expected 32\n",
                pixGetDepth(g.marge));
    pix1 = pixConvertRGBToLuminance(g.marge);
    if (!pix1) fail_hard("pixConvertRGBToLuminance(marge)");
    g.gray_page = pixScaleGrayLI(pix1, 2.0f, 2.0f);
    pixDestroy(&pix1);
    if (!g.gray_page) fail_hard("pixScaleGrayLI(luminance(marge), 2, 2)");

    /* Secondary 8 bpp page (reported, not used by the fixed case table) */
    g.gray_lucasta = load_pix(dir, "lucasta.150.jpg", 0);
    if (g.gray_lucasta) {
        pix1 = g.gray_lucasta;
        g.gray_lucasta = pixConvertTo8(pix1, 0);
        pixDestroy(&pix1);
        if (!g.gray_lucasta) fail_hard("pixConvertTo8(lucasta.150.jpg, 0)");
    }

    /* Secondary 32 bpp image (reported, not used by the fixed case table) */
    g.test24 = load_pix(dir, "test24.jpg", 0);

    /* Colormap-bearing image */
    g.cmapped = load_pix(dir, "weasel-64g.png", 1);

    /* Seeds for the seedfill cases */
    w = pixGetWidth(g.feyn);
    h = pixGetHeight(g.feyn);
    g.seed_bin = pixCreate(w, h, 1);
    if (!g.seed_bin) fail_hard("pixCreate(seed_bin)");
    pixSetPixel(g.seed_bin, 200, 200, 1);
    pixSetPixel(g.seed_bin, 1300, 1700, 1);
    pixSetPixel(g.seed_bin, 2500, 3200, 1);

    g.seed_gray = pixCreate(w, h, 8);
    if (!g.seed_gray) fail_hard("pixCreate(seed_gray)");
    pixSetPixel(g.seed_gray, 200, 200, 64);
    pixSetPixel(g.seed_gray, 1300, 1700, 64);
    pixSetPixel(g.seed_gray, 2500, 3200, 64);
    /* mask: background -> 128, text strokes -> 255; seed (64) <= mask */
    g.mask_gray = pixConvert1To8(NULL, g.feyn, 128, 255);
    if (!g.mask_gray) fail_hard("pixConvert1To8(feyn, 128, 255)");

    /* sel and kernel, made once.  Note: selCreateFromString requires
     * strlen(text) == w*h exactly and exactly one uppercase origin marker,
     * so the rows are given without separators:
     * row0="xxx", row1="xXx" (X = origin), row2="xoo". */
    g.sel_hmt = selCreateFromString("xxxxXxxoo", 3, 3, "corner3x3");
    if (!g.sel_hmt) fail_hard("selCreateFromString(corner 3x3)");
    g.kel_gauss = makeGaussianKernel(3, 3, 1.0f, 1.0f);
    if (!g.kel_gauss) fail_hard("makeGaussianKernel(3, 3, 1.0, 1.0)");

    /* Fixed affine point pairs, from the size of gray8 */
    w = pixGetWidth(g.gray8);
    h = pixGetHeight(g.gray8);
    g.ptas = ptaCreate(3);
    ptaAddPt(g.ptas, (l_float32)0.0, (l_float32)0.0);
    ptaAddPt(g.ptas, (l_float32)(w - 1), (l_float32)0.0);
    ptaAddPt(g.ptas, (l_float32)0.0, (l_float32)(h - 1));
    g.ptad = ptaCreate(3);
    ptaAddPt(g.ptad, (l_float32)(0.02 * w), (l_float32)0.0);
    ptaAddPt(g.ptad, (l_float32)(w - 1), (l_float32)(0.02 * w));
    ptaAddPt(g.ptad, (l_float32)0.0, (l_float32)(h - 1));
    if (!g.ptas || !g.ptad) fail_hard("ptaCreate(affine pairs)");

    /* Compressed buffers for the decode cases (setup, never timed) */
    if (pixWriteMem(&g.png32, &g.png32_size, g.marge, IFF_PNG) != 0)
        fprintf(stderr, "warning: PNG encode of marge failed; "
                "png_decode_32 will report FAILED\n");
    if (pixWriteMem(&g.tiffg4, &g.tiffg4_size, g.feyn, IFF_TIFF_G4) != 0)
        fprintf(stderr, "warning: G4 encode of feyn failed; "
                "tiff_g4_decode will report FAILED\n");
    g.jpgbytes = read_file_bytes(dir, "marge.jpg", &g.jpgbytes_size);
    if (!g.jpgbytes)
        fprintf(stderr, "warning: cannot read marge.jpg bytes; "
                "jpeg_decode_32 will report FAILED\n");
}

static void free_inputs(void)
{
    pixDestroy(&g.feyn);
    pixDestroy(&g.feyn_t3);
    pixDestroy(&g.feyn_ropdst);
    pixDestroy(&g.feyn_xordst);
    pixDestroy(&g.gray8);
    pixDestroy(&g.gray_page);
    pixDestroy(&g.gray_lucasta);
    pixDestroy(&g.marge);
    pixDestroy(&g.test24);
    pixDestroy(&g.cmapped);
    pixDestroy(&g.seed_bin);
    pixDestroy(&g.seed_gray);
    pixDestroy(&g.mask_gray);
    selDestroy(&g.sel_hmt);
    kernelDestroy(&g.kel_gauss);
    ptaDestroy(&g.ptas);
    ptaDestroy(&g.ptad);
    lept_free(g.png32);     g.png32 = NULL;
    lept_free(g.tiffg4);    g.tiffg4 = NULL;
    free(g.jpgbytes);       g.jpgbytes = NULL;
}

static void print_inputs(void)
{
    printf("inputs (loaded once, never inside the timed loops):\n");
    printf("  %-13s %-20s %6s %6s %3s\n", "label", "source", "w", "h", "d");
#define PRINT_PIX(lbl, src, p)                                        \
    do {                                                              \
        if (p)                                                        \
            printf("  %-13s %-20s %6d %6d %3d\n", lbl, src,           \
                   pixGetWidth(p), pixGetHeight(p), pixGetDepth(p)); \
        else                                                          \
            printf("  %-13s %-20s %6s %6s %3s\n", lbl, src,           \
                   "(not", "loaded)", "");                            \
    } while (0)
    PRINT_PIX("feyn",         "feyn.tif",            g.feyn);
    PRINT_PIX("feyn_t3",      "feyn+(3,3)",          g.feyn_t3);
    PRINT_PIX("gray8",        "test8.jpg",           g.gray8);
    PRINT_PIX("gray_page",    "marge.jpg lum x2",    g.gray_page);
    PRINT_PIX("gray_lucasta", "lucasta.150.jpg",     g.gray_lucasta);
    PRINT_PIX("marge",        "marge.jpg",           g.marge);
    PRINT_PIX("test24",       "test24.jpg",          g.test24);
    PRINT_PIX("cmapped",      "weasel-64g.png",      g.cmapped);
    PRINT_PIX("seed_bin",     "(derived)",           g.seed_bin);
    PRINT_PIX("seed_gray",    "(derived)",           g.seed_gray);
    PRINT_PIX("mask_gray",    "(derived)",           g.mask_gray);
#undef PRINT_PIX
    printf("  %-13s %-20s %19zu bytes\n", "png32",  "(setup buffer)",
           g.png32_size);
    printf("  %-13s %-20s %19zu bytes\n", "tiffg4", "(setup buffer)",
           g.tiffg4_size);
    printf("  %-13s %-20s %19zu bytes\n", "jpg",    "marge.jpg bytes",
           g.jpgbytes_size);
}

/* ------------------------------------------------------------------ */
/* Timing framework                                                    */
/* ------------------------------------------------------------------ */

typedef struct {
    int    failed;
    int    runs;
    double min_ms, median_ms, mean_ms, mad_ms;
} CaseResult;

static int cmp_double(const void *a, const void *b)
{
    double x = *(const double *)a;
    double y = *(const double *)b;
    return (x > y) - (x < y);
}

static double median_sorted(const double *v, int n)
{
    if (n & 1)
        return v[n / 2];
    return 0.5 * (v[n / 2 - 1] + v[n / 2]);
}

/* 3 warm-up runs, then >= 10 measured runs covering >= min_time_ms.
 * Each measured run times exactly one call to bc->run, which destroys
 * its own results.  File I/O and decoding happen only in the I/O cases. */
static CaseResult run_case(const BenchCase *bc, double min_time_ms)
{
    CaseResult r;
    double *samples;
    int cap = 64, n = 0, i;
    double t_case0, total = 0.0;

    memset(&r, 0, sizeof(r));
    samples = (double *)malloc((size_t)cap * sizeof(double));
    if (!samples) fail_hard("out of memory");

    for (i = 0; i < 3; i++) {           /* warm-up, not measured */
        g.fail = 0;
        bc->run(&g);
        if (g.fail) {
            free(samples);
            r.failed = 1;
            return r;
        }
    }

    t_case0 = now_ms();
    do {
        double t0 = now_ms();
        double dt;

        g.fail = 0;
        bc->run(&g);
        dt = now_ms() - t0;
        if (g.fail) {                   /* discard partial data, mark failed */
            free(samples);
            r.failed = 1;
            return r;
        }
        if (n == cap) {
            double *ns;
            cap *= 2;
            ns = (double *)realloc(samples, (size_t)cap * sizeof(double));
            if (!ns) {
                free(samples);
                fail_hard("out of memory");
            }
            samples = ns;
        }
        samples[n++] = dt;
        total = now_ms() - t_case0;
    } while (n < 10 || total < min_time_ms);

    r.runs = n;
    r.min_ms = samples[0];
    {
        double sum = 0.0;
        for (i = 0; i < n; i++)
            sum += samples[i];
        r.mean_ms = sum / (double)n;
    }
    qsort(samples, (size_t)n, sizeof(double), cmp_double);
    r.median_ms = median_sorted(samples, n);
    for (i = 0; i < n; i++) {           /* deviations from the median */
        double d = samples[i] - r.median_ms;
        samples[i] = (d < 0.0) ? -d : d;
    }
    qsort(samples, (size_t)n, sizeof(double), cmp_double);
    r.mad_ms = median_sorted(samples, n);
    free(samples);
    return r;
}

/* ------------------------------------------------------------------ */
/* Output                                                              */
/* ------------------------------------------------------------------ */

/* CSV-quote a field when it contains a comma, quote or newline. */
static void csv_field(FILE *fp, const char *s)
{
    if (s && (strchr(s, ',') || strchr(s, '"') || strchr(s, '\n'))) {
        fputc('"', fp);
        for (; *s; s++) {
            if (*s == '"')
                fputc('"', fp);
            fputc(*s, fp);
        }
        fputc('"', fp);
    } else {
        fputs(s ? s : "", fp);
    }
}

static void write_csv(const char *path, const char *banner,
                      const BenchCase **sel, const CaseResult *res, int ns)
{
    FILE *fp = fopen(path, "w");
    int i;

    if (!fp) {
        fprintf(stderr, "error: cannot open %s for writing\n", path);
        exit(2);
    }
    fprintf(fp, "%s\n", banner);
    fputs("case,function,input,width,height,depth,params,runs,"
          "min_ms,median_ms,mean_ms,mad_ms\n", fp);
    for (i = 0; i < ns; i++) {
        const BenchCase *bc = sel[i];
        const CaseResult *r = &res[i];
        PIX *pix = bc->pixpp ? *bc->pixpp : NULL;

        csv_field(fp, bc->name);   fputc(',', fp);
        csv_field(fp, bc->function); fputc(',', fp);
        csv_field(fp, bc->input);  fputc(',', fp);
        fprintf(fp, "%d,%d,%d,", pix ? pixGetWidth(pix) : 0,
                pix ? pixGetHeight(pix) : 0,
                pix ? pixGetDepth(pix) : 0);
        csv_field(fp, bc->params); fputc(',', fp);
        if (r->failed) {
            fputs("0,,,\n", fp);   /* runs=0, empty stats */
        } else {
            fprintf(fp, "%d,%.3f,%.3f,%.3f,%.3f\n", r->runs, r->min_ms,
                    r->median_ms, r->mean_ms, r->mad_ms);
        }
    }
    fclose(fp);
}

static void print_results(const BenchCase **sel, const CaseResult *res, int ns)
{
    int i;

    printf("\n%-17s %-25s %-20s %5s %5s %3s %6s %10s %10s %10s %10s\n",
           "case", "function", "input", "w", "h", "d", "runs",
           "min_ms", "median_ms", "mean_ms", "mad_ms");
    for (i = 0; i < ns; i++) {
        const BenchCase *bc = sel[i];
        const CaseResult *r = &res[i];
        PIX *pix = bc->pixpp ? *bc->pixpp : NULL;

        printf("%-17s %-25s %-20s %5d %5d %3d ",
               bc->name, bc->function, bc->input,
               pix ? pixGetWidth(pix) : 0,
               pix ? pixGetHeight(pix) : 0,
               pix ? pixGetDepth(pix) : 0);
        if (r->failed) {
            printf("   FAILED\n");
        } else {
            printf("%6d %10.3f %10.3f %10.3f %10.3f\n", r->runs,
                   r->min_ms, r->median_ms, r->mean_ms, r->mad_ms);
        }
    }
}

/* ------------------------------------------------------------------ */
/* main                                                                */
/* ------------------------------------------------------------------ */

static void usage(void)
{
    fprintf(stderr,
        "usage: bench_native <prog_dir> [--out results.csv]\n"
        "                     [--filter <substring>] [--min-time-ms N]\n"
        "                     [--threads 1]\n"
        "\n"
        "  <prog_dir>        directory with the leptonica test images\n"
        "                    (e.g. .../leptonica/upstream/prog)\n"
        "  --out PATH        CSV output path (default: results.csv)\n"
        "  --filter SUBSTR   run only cases whose name contains SUBSTR\n"
        "  --min-time-ms N   minimum measured time per case (default: 1000)\n"
        "  --threads N       accepted for CLI parity; leptonica is\n"
        "                    single-threaded, only N=1 has any effect\n");
}

int main(int argc, char **argv)
{
    const char *prog_dir = NULL;
    const char *out_path = "results.csv";
    const char *filter = NULL;
    double min_time_ms = 1000.0;
    int threads = 1;
    char date[64], cpu[256], os[256], banner[1024];
    char *leptver;
    const BenchCase **sel;
    CaseResult *res;
    int ns = 0, i, n_failed = 0;

    /* Silence the library before anything else can print or write files. */
    setMsgSeverity(L_SEVERITY_NONE);
    setLeptDebugOK(0);

    for (i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--out") == 0 && i + 1 < argc) {
            out_path = argv[++i];
        } else if (strcmp(argv[i], "--filter") == 0 && i + 1 < argc) {
            filter = argv[++i];
        } else if (strcmp(argv[i], "--min-time-ms") == 0 && i + 1 < argc) {
            min_time_ms = strtod(argv[++i], NULL);
            if (!(min_time_ms > 0.0)) {
                fprintf(stderr, "error: --min-time-ms must be > 0\n");
                return 2;
            }
        } else if (strcmp(argv[i], "--threads") == 0 && i + 1 < argc) {
            threads = atoi(argv[++i]);
            if (threads != 1)
                fprintf(stderr,
                    "warning: leptonica 1.87.0 is single-threaded; "
                    "ignoring --threads %d\n", threads);
        } else if (argv[i][0] == '-' ) {
            fprintf(stderr, "error: unknown option %s\n", argv[i]);
            usage();
            return 2;
        } else if (!prog_dir) {
            prog_dir = argv[i];
        } else {
            fprintf(stderr, "error: unexpected argument %s\n", argv[i]);
            usage();
            return 2;
        }
    }
    if (!prog_dir) {
        usage();
        return 2;
    }

    leptver = getLeptonicaVersion();
    get_date(date, sizeof(date));
    get_cpu_name(cpu, sizeof(cpu));
    get_os_name(os, sizeof(os));
    snprintf(banner, sizeof(banner),
             "# date=%s; cpu=%s; os=%s; compiler=%s; leptonica=%s",
             date, cpu, os, compiler_id(), leptver ? leptver : "?");

    printf("%s\n", banner);
    printf("bench_native: fixed-operation benchmark for leptonica %s\n",
           leptver ? leptver : "?");
    printf("  prog_dir      : %s\n", prog_dir);
    printf("  out           : %s\n", out_path);
    printf("  filter        : %s\n", filter ? filter : "(none)");
    printf("  min-time-ms   : %.1f\n", min_time_ms);
    printf("  threads       : 1 (leptonica is single-threaded)\n\n");

    load_inputs(prog_dir);
    print_inputs();

    sel = (const BenchCase **)malloc(sizeof(*sel) * (size_t)ncases);
    res = (CaseResult *)malloc(sizeof(*res) * (size_t)ncases);
    if (!sel || !res) fail_hard("out of memory");

    for (i = 0; i < ncases; i++) {
        if (filter && !strstr(cases[i].name, filter))
            continue;
        sel[ns++] = &cases[i];
    }
    if (ns == 0) {
        fprintf(stderr, "error: no case matches filter '%s'\n", filter);
        return 1;
    }

    printf("\nrunning %d of %d cases (3 warm-up runs each, then >= 10 "
           "measured runs covering >= %.0f ms):\n", ns, ncases, min_time_ms);
    for (i = 0; i < ns; i++) {
        res[i] = run_case(sel[i], min_time_ms);
        if (res[i].failed) {
            n_failed++;
            fprintf(stderr, "  %-17s FAILED\n", sel[i]->name);
        } else {
            fprintf(stderr, "  %-17s %5d runs, median %9.3f ms\n",
                    sel[i]->name, res[i].runs, res[i].median_ms);
        }
    }

    print_results(sel, res, ns);
    write_csv(out_path, banner, sel, res, ns);
    printf("\nwrote %s (%d cases, %d failed)\n", out_path, ns, n_failed);

    free(sel);
    free(res);
    free_inputs();
    lept_free(leptver);
    return 0;
}
