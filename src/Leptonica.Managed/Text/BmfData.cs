namespace Leptonica;

/*
 *  bmfdata.h: data for constructing the bitmap fonts.
 *
 *  The C header holds the nine font images (the 95 printable ascii characters
 *  of Palatino-Roman in three rows, at 4 to 20 pts and 300 ppi) as base64
 *  strings of ccitt-g4 tiff files.  The tiff reader (tiffio.c) is not ported
 *  yet, so these are the same images as base64 strings of 1 bpp png files,
 *  made from the tiff images of bmfdata.h by lossless conversion.
 *  TODO(tiffio.c): replace by the base64 tiff strings of bmfdata.h and read them
 *  with pixReadMem().
 */
internal static class BmfData
{
    internal const int NUM_FONTS = 9;

    internal static readonly string[] InputFonts = {
        "chars-4.tif", "chars-6.tif", "chars-8.tif", "chars-10.tif", "chars-12.tif",
        "chars-14.tif", "chars-16.tif", "chars-18.tif", "chars-20.tif"
    };

    internal static readonly string[] OutputFonts = {
        "chars-4.pa", "chars-6.pa", "chars-8.pa", "chars-10.pa", "chars-12.pa",
        "chars-14.pa", "chars-16.pa", "chars-18.pa", "chars-20.pa"
    };

    /* Baselines of the three rows of each font, as stored in the pixa files */
    internal static readonly int[,] Baselines = {
        {11, 12, 12}, {18, 18, 18}, {24, 24, 24}, {30, 30, 30}, {36, 36, 36},
        {42, 42, 42}, {48, 48, 48}, {54, 54, 54}, {60, 60, 60}
    };

    internal const string FontData4 =

        "iVBORw0KGgoAAAANSUhEUgAAA5IAAAI2AQAAAAAK8IrzAAAHXElEQVR42u3dT4gb1wHH8e9I8q4SjK2G4toQkApucSEH91LckrBD" +
        "6a0tzXGhhag00B5MCeSSlnT13ITUl8IWcgiF1kvJIVAoPuYQ4vHWEB9Cs4FSXGJqOQXHlBDL66U72h3Nr4fRrKS11l6vYyeU3zuM" +
        "RqM389H7M290WH4biYddzld4+MWmTZs2bdq0adOmTZs2bdq0adOmTZs2bdq0adOmTZs2bdq0+VmbQl3WoctqD5YgJmWFib9dCFwc" +
        "f/MydHkv0KC/NzOHHsAxbgGLACktsjBR92MGgWXUQBCgzSqapxr22rcNQCs82oMYYrrUyceuJngFwZfgEsCLKOFfsEgW7308e+VO" +
        "FwUaZNRi0rHujKnBBWgUnS4g3M8cilr8PlAn78IKESRAlkzUrQG0iXpkABk8A8/d17x9NhCzD/hCAFrUmZns27JcpwKnWW8B0Rt7" +
        "NSsAdbjOgddRt/wwmtJ1AzhBBieJzpJDun2m3VM7Xw3RW3AN/XbsIo8Ag1YxtcsTLtOD01RagH7Evr2ZEcD3A4fRq3B87PN1oNod" +
        "O5BBg/7S1pvXmA2fwjp0aFrFtJhWxURutjU21eM9rkNAhlLUQ3lxw5STJwBL1Ibd+z75WmHP9KjDi0WFeyqSpEyS/qlsVoN/K6cj" +
        "6YpuSdJ68dkZfST9RmlToZM1lTV1UNmc3pMOqq97KwkTb8+Mdq9oXaPvI93Qp1WSyfEcG5nG1g3zgJ9lrfE5M1z28m3f4QE+Pxul" +
        "Unt4z+y6f5vYtGnTpk2bNm3atGnTpk2bNm3atGnTpk2bNm3atGnTpk2bNm3atGnTpk2bNm3atGnTpk2bNm3atGnTpk2bNm3atGnT" +
        "pk2bNm3atGnTpk2bNm1+Pk217mLehGXIIIKrQVHgVEwUOIUO1nmZq/SfCoMWN2lxehnV6dfKbYvzDeqnWb7AhSsxcaw4Joq5m5kF" +
        "fgg5vBu4kUR/TPgp/A29HaLfvcazHGHmqVBtsV8t2t9g8zIzPx9udZbHu1yc56tHORrFXEkiYhbiyUi4yrTu1k8CKVSgF4jaEIjI" +
        "k1GVDQACZLBxCOrDbV62Z38dzsICPMNErCNMpH/lFSCr0YCfzQ+PhQR6EEMYjAWirZU7M5QJhsW2QatassDX4fiPCduy1Ip2DiAu" +
        "LjQAQn41sAl5UMxWINtgLPDxOkB0Dqqlv7atz7rQhEOg4po79O0/AGZiRPT+ZGsIw2ZcHbIr27pqA2rwLoNy3GYa8GYA+GI5EtvN" +
        "KiTMM6gRPU0OS7AaOFA2rA+rADQnRmYBKsWBvwNwkdrKtJtxbcd2qk01g3kyIPAhROWsmYUD0B+Bx8v+G1Y5AUBMtTyj2kUNyKEx" +
        "HImp5vD0LqD1mCZQxCGuFPNWM6OzDgPwJNAmiwCyCA5PBOPN/JKxRNAdxnORQQ1eoE7lUkI3YZjd1yq+yHi+3X5QmcbYyMrt/rHL" +
        "JhDBX4vRT9hp3h6lmjFIWCM6FAMo5mlQUjS3MnZfF00+Bhxn338AZjZghqwHkHYBonrZ7KXblsIi/zDvaCBJm9ItKZGaTQUpmRNS" +
        "0tSsRCc/MKuX1Ff6ZCdH6ScH9c5Hyg4qnVM2q/SEenNK59SdU97RYFNa6Oi8cubUmZbHmHd2nac4PfOxeadTJq4+ymMM9/co0of3" +
        "/iy7h39H/PG0g9GNO54zMYsi/+9jmzZt2rRp06ZNmzZt2rRp06ZNmzZt2rRp06ZNmzZt2rRp06ZNmzZt2rRp06ZNmzZt2rRp06ZN" +
        "mzZt2rRp06ZNmzZt2rRp06ZNmzZt2rRp06bN/wNzED6DdiZFNFsGZV5fDEWOWR/WGDC4PZ9ux9IlZzIOcYpZiYvwtgzKpLAAqE2Z" +
        "s0YF6rs3uVPg2NZ4Xp726fC8/VR3BsIe+zYaxuP1Hu68XQaoM4hZhdP0A6sQeAUWWWOjyOLbOIKOQp1bLfoxx+gHEv6AjtB/LuZb" +
        "3LzI1XjXZguggdp8AF0EH5An/BkuFRVSQH8J/BedREkR4CfUBaKvUHsBfs0jxziwe/N88VKDx9AiaeCxUEn4LixSpwZngX1fI3qd" +
        "fIFHG6SBNyBEbb4NC0DMZWq9UYrjbZmIt5mnihcFWsMZ+qfJmkuj3dEE7m/N1MpJOIEgGk6rFqvJ7tahzVG9ZgDe2nrbnnZ6fTRr" +
        "NdzNhmaXfbs0K9vm//zW3vFpt8fs8PUJuAZvUmmPpWl+OexyvU2KKL80FMhIOjw6ZwN6pKG8uXKAKIInANaSu94sRV7hVnZh+gO9" +
        "JM2qd0ZdZR1FSp/XTQ2UPa+bSt+Zyx+XZpXOqXdOB9WVmlru6Ib0K+W/kDpK5nROA+ncTkGJyXZze/lm8TLYdnhTO1y0o/KTnc2i" +
        "bwUwqE3phu88sHUoB6hm9385ha315W5rfHXHCm9v3QC7KhujNS3Z8++E741fa1SuDR9020t/N1/MuZM2bdq0adOmTZs2bdq0adOm" +
        "TZs2bdq0adOmTZs2bdq0adOmTZs2bdq0adOmTZs2bdq0adOmTZs2bdr8PJv/A8INQuVt6BTRAAAAAElFTkSuQmCC";


    internal const string FontData6 =

        "iVBORw0KGgoAAAANSUhEUgAABEEAAAI6AQAAAADgL2tBAAALMElEQVR42u3db4gcdx3H8ffsjrl9cObW+sBrrb3psyJaIq0l1TQ7" +
        "LYp54IMgPhCtckrFPLGcKBJFe7/UYIK2Jg98IKWSRUEKKlSQ0kJoJm2loQjZ0qJVazPXBHvFkOzl1mb3sjtfH8zs7uze7fXucmlP" +
        "/Pwe3Mztzp/X/P5857dPvj/P2BrlQIGtUiSRRBJJJJFEEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRBJJ" +
        "JJFEEkkkkUQSSSSR5NpJ6ltG0uzvLkGNePiApWss6ThugF/C3+FcGyyAJtSow/zwKc9AA+YcCw6qtHw4R8cLeQZe34Q6CQA44Xii" +
        "CckMFKDKJOaGK+0fUINqRD2C9NvzQMiX4Ldus1rnCfi1D50Q6hAyDqeGj/mVYy/cFHMxxqDYhhm8xNnXHO/fYJ34uX0HHhYC9cns" +
        "SwPwPjdwSmMcImK4GcouqxMLISkCVDenx96YgkpALd2wbfmIsrBHh6T3sbdpY6dA5zMhHhCDVaGe3sZ+NnDKfPeeIQTd/cRRaMPT" +
        "blMkn6J4Z0QEXy737uMw+MLo86skYOXsWt6LmxlPavDhCPhWuVfrgw9azjEspAlJk/ZBaMJ34czVSgrZ7bwW7Aqg6fp9ujRwStTb" +
        "C1mq4kPxOFyABtwF929anUQOvud468/1FWqhH3YAc4zNUAc/gEMA7Hd8fFMkrecdd0TwyYg3B794XwjMhb0RA2BASAko4/kwj/kR" +
        "uzdFsu3bDg+sMc3g+2bsYgTE4cDRbQBevC/30T39Ib5hST4YJA745vKDKy4LJtGKr8LaVYWUZf3EMlXzE6uclT53AyKoPJqL0d3H" +
        "uKp3ce4ubYAPDbQBcC6LH6775PMQ1wDmGxQ7EICF/QttuE66j+IchfEqN8TloWY714/6rzmAP5TZUQUYn4fvY+A97TY8fVk+in8K" +
        "zfeETNxbzkWZ3ID2gK/DwyEOFkI699HZS+HnJCF8Gl7eaEex4fKs2WUzMzthZsmsLXa/OJ1uWrZ6OWEbKm55ndzejanh0IiqrW0O" +
        "Hm5a69SHQntj9Dz3Gs+oJwf+m+6/dLIp0Pi79Hsn7NdR9O7+8gq20G/AYMtI9LtYEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRBJJ" +
        "JJFEEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRBJJJJFEEkkkkUQSSSSRRJL/" +
        "bckz76wkHvq/TrqOgIvXUCedMpx00ICFAix4dLyAlhdwwIeTZeYCj4UyB1zHn2OhDAd5hpabg4Uy5oVYAeZ8mAu4ucBC+UEWgsPM" +
        "zc4GTLZZiuq0DoRZ9nxghTzDaWmPmR2ZMjtr1m6azdXNKkfMKsds8YrZ1Glr2xtZFuJjpy2ZNTtrRyypXDF79lmzY1PW3D9rrbrZ" +
        "kWO264eWzF629uxpayazFTu7aEnlDbuSVOz5VfIMZ8DjWP3xdN+HC+8ll+a+Mx3kj60NtsOrtwIzXPlxBCXYMVjxCbDdx4ubkIS5" +
        "NPEj+8kOEndLb22BGqR5fQNYlvC+mt3CwQxQHQNCGl4MPjgI07sUojIQhdAEF+WSbq8gWere1wfv0q7+UhmF7HFC8GkP5JnPUqaX" +
        "CGEngJ9BZ9LNdD/7e4TfNhiLoV7Fx42WpDduAA3aL5VgAmhnCbxLgIPmUBruLFG52QBsaLkPAI8ADmSPujOE1op10oGj8BsAPpp+" +
        "9QLwHNBaPcVvp9ubXLq2BIWsOUPoDPYiB4UY/Gqaqf3SipK2A+wXAIyHMA9HgcANpPw/A0k9e8JgoEXTets+2Hf4CSwMdqomjgJw" +
        "o8syoS+T+I6QpWk6XkCxlibI3gkPAhf7h7+eLrgCMBtln13IrpQAY9lnrayJHoKo2jt7JqTgZ+skbB8Z7YuwgzcdRYvhj+DgJiCM" +
        "YLz/VBUoZnXiunUykW1P5jrrWLbdD7vq/b7d7Vlxmof81Oix869se2uaftzHHkyPGYzXieVXi8igXjsdHd0M2k3SrlLsP0fo4PG7" +
        "0yhQIm3+EZIbsu2lNEA0YXagibtLnXiH85eIei2XXq6b9b+W9uCwd+RbQKkJ7bSaLBgt+YCj4wXwEXD4n7+395JyA8eWBhK2e3Gv" +
        "JfDiXvr3YKg31OFmIG7D0s4sFo5+A26rUrSYzg4Au2NPGjjJErzXu69VG+xrM+lYaWT7cWppE4CRj15t8MpBf4GbcFQ8qeHtSyNb" +
        "BCGFp17Gow6JS2Pnudw4y5eduevuhJkkGxxRb9EaIMpF6/qN/cCxYjyJ4IsAvAQG3tHH0jfDf+D6FrCnd9JAV0vDWX/Nk9vPp8PH" +
        "UvOxKoBXq8F13UieNlprMD18bxpgdqS7tMBps6RiFlWs/kDRmkWzpmcJFWszZW7WzE1YPIXVJ2zR2sW6tanYjy561qycNatPWLto" +
        "1sYs2l0xzOoTZq5itmhmlUWzY3WzqGIxU90lDcxcfn5ybAPLFywO/X+lv3toLeefXnF+Ut2EiaI//F5/m9JcceyEV3fn4bnWifJG" +
        "5/a2kTqYHPmNd3wt5y+tJBmaia2trPLcxfo7+nsnvsrzd+s3oCSSSCKJJJJIIokkkkgiiSSSSCKJJJJIIokkkkgiiSSSSCKJJJJI" +
        "IokkkkgiiSSSSCKJJJJIIokkkkgiiSSSSCKJJJJIIokkkkgiiSSSSCKJJJJIIokkkkgiiSSSSCKJJJJIIokk/8+SblbtLVAncXcn" +
        "gjRzOt1s6x0H/Szqh2Fhvbcxh71NbtueZCzo5oOPc1+nKW/bkKaXXYIQlyWFXk9Jk2UX19lPqgA70v3x7hPl0svGvWzLK91yU3ps" +
        "Mt199qFSX3v/bm/O2EkrYpWu6zOYQnp5aW6KpHOV9QtQ2hRJIU1f7Gg5KMNhONXN2X8Q0h4bZWnpa1kF+PCUg3FsH50AoFbj5Ou0" +
        "plrOKiEHD7NQWq8krdkiXIywBnYe+x1cjvgb/D5gIWSejssa6M6PhTxcxu6nsz8i+QtLr9J5nMshYda8+0niENdm8fh6JY3xbhVP" +
        "YzHJYZKHYD4mwvYEXI6ZpAg8CXDirzFffpLkEMUzMcl1bGuwFFCHKOK269j2lXFXfMDx3Gfxxzcc7cvkMtPXHN8gORhRyoZxGnZK" +
        "hx2FgI7vKM/QTLtImesjgMSHyEsjYPlte/lyyXxuuZVCiY7fXYCi7+tHKcq9BN1BSAMI+529VMdrH4c7IIiYPJpdqOCYa6xFsqMr" +
        "abncp191Q6d0Rl6gt5yAlaBYgADawcAqA+e/s67W+Xf+5lMxwF39D0bH7FI3BPoxvJIOgLKDMDsncbz02Lok55cf9kJu34262Fgz" +
        "15L+OXgt35wATNfXPT9ZwtLIPwPg7ep/4+U7oKWxJQ3yzd7L08GMg0lIwjW+AVaWOPDx86+R/CtgOh//E7ds1ZWIQpw21RkYg1Zj" +
        "w5LLcOEV2IX/A9hZBexAroZvyR97F5yO2F4DaM9jjsvZoXvgoAM3uJDL+iQf3IftpXiKwtGQ66s8AmD75nnDAY8GNEJu2zfPI0sU" +
        "X4BLjrG9LFXp7MWHpf14aS127onw4Z/ltc2muqWy6noFuyujD6ussB5Cr3S66z30/q5c3Bpn1J27N/qKXfOEZUiyMCrxv3sH5/YA" +
        "TJi7JjdIY1BnnXP7lbtTOLhdc2nmYlBhbT121ZLs6u7YOnvsoTUuAbLWHuv9Kdu5skJvrgEPX/V00jO2RjmgX+iSSCKJJJJIIokk" +
        "kkgiiSSSSCKJJJJIIokkkkgiiSSSSCKJJJJIIokkkkgiiSSSSCKJJJJIIokkkmwdyX8BqYjanvlrtBIAAAAASUVORK5CYII=";


    internal const string FontData8 =

        "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" +
        "8MAZ1DV6LSYAAAAASUVORK5CYII=";


    internal const string FontData10 =

        "iVBORw0KGgoAAAANSUhEUgAABagAAAJCAQAAAAANJx2WAAATgUlEQVR42u2dQYwcx3VAX880dhoIw2knh2wCQtMODCMHB6FOoQKK" +
        "3YaMwAfD8TGXQHROuZkGbIQWNpyiLVg8BPAefdMEyEG3ONfEknpFIt6DAa8THXwI7F5yLa8dJe5dTqyeVU//HKq7Z3pmdneWu0NK" +
        "wa/DTk93ddWrX///+lUD7HeEj2FpodRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRK" +
        "rdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRKrdRK/VGnHp/h/fEzp97fmX4iprpKoQCzuAHzTLClLLnI/R+IPH4skotIGuYi" +
        "IvdFRDoiIylCEXlbZktWfo5ExIhIHIrII5GEvkjRl7R331ZI+nJxpZb1EIghTWHow+MvAfAWgAdHODeBLy0cdwvYBzERxAmQQAJw" +
        "GMPuDdiK4J8Gq9CQFIjs5c4Q9q6NAfn1ZEwppP7c+yMovgIEUJCASYEH5cMfx7aHLyaweWvF1vjDn1ZXBQA+JHANfrLYIP/eMAby" +
        "2ynyXDal6/9WTsbzt5Dg2oqoPwXgwqYP21eOgPweIMA2XIHvB3PvH8IRMcObwLeh+PKGfaEjBr4LbdmjiDIK8lVQl5PvJghes07N" +
        "emNhE2kr4bcMOE5WitmAB0gAkJBXqrcyDUlqyiGAU97ehMvwp/P65NpX3PpGVOkVFFFpy0AeBaui/qMpZbYlqzsHcGNgMDdK0rIp" +
        "Y0dZDTU3WMc01+xKVvQ1rC8rHxvEqvdwfqa9iZyzqn4+dcPFn4CvhHrdZ/TCC2ZKiHytXLQD8ODH8/KKJ0yXKpWp2jwAPAbWmFdD" +
        "7TTvX3N3KoG3ytk1vLO4jcar21bGUlm0AIm/Wmvs/OhHxorZA3B3Gpb2hWTu/c0ZdcG3dnx0BA9L83T3Byv2IXFsjevKzsA6vXtT" +
        "D6+e0pixxuoBmQB3rRG2RtVSv0JrbA0j+O2BtSvHWL9VRilzMUjDn/nw36VJtGMDxkDlWFYdXztsQ/ftCMiShmfmeyeHj8C7O3bB" +
        "unQV3iS2DsUMcJN4xZ7PHNXrdbrbMP95zxfNLKLdoXUqvhfbQY8Asw/pYMXUtwQo+h78HP9kXW64wiKCq7FdhwKg7VqNkus5rYNo" +
        "FdRTscel2ABH/wnx7TktcL7sA2y5823lZmYcLRteFS+Bw1+vTtYHX/1q6SvGo8tw77RALTfNWBsCJCrNwC0/c3MHtla+R7dy7yyK" +
        "8hx5Pa2N85i27n4jmiw/San9N1ZCbS+6vV75/egQoHd4zHthNhHv9CZuLkzahhHB4mGen3pxo/4SW/BcygV7/wRfc7GxyAINGZuJ" +
        "4Lx/rIOk/LjXa8QdW6ktr5nJbbOKI4iTz54y1+NEATYWy8GkQTG5HYL4M5uli6WeVuDMVNPdWqw+QTUMd+qrROU+HXBiDFGJLDyc" +
        "Xfsvirr8j3dBUIWel0ewUVnYomMySaY3bFB81p0aoGOHUwTg4U7viC76PAREKrnvABTFOFt0NhZMq0dRAuVRQbsyuptHU7tkPBiu" +
        "Wq8HewCdQwiHsQ/QSmysv9Ax+Bh43dgKvU2QV2D/XbhcROQRuGYH/mMl/voTk1t7nwcGa5+B4HeD9UU7lgjAKe2r83UYm00r+oc3" +
        "4VAiRp+HztftUFqvD5AvrOR0cqrEfREJRUSybh6KiEhP5GDqSLI+aeyJSNGZetcpP5NQpGiXX4xIcZGHk7KIOp+/9R2RR/MHqZb6" +
        "GZQn+q1gNH8s9XTLktQR7B2ze/kIU2eNAPzwxPXyGVG3529daRwg1UulYz7SsvYbccQ6z7gsSb02eyr5saBul/uqj0hx9P8EK7VS" +
        "K7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VS" +
        "K7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSK7VSP9Vy4J2V" +
        "elBfbfnzT7e85VoZTbpzovplM1fvkTfmUQCMI/JF2ZlOSgWV9kQk7pVJqgRCkQJHxEk6IimEEuOIFCASt3N6YjqSIDlhEorpiOSO" +
        "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" +
        "dB7p7WsXc84cJEtU2grPfn7dXeSvt5/mEXocnONldwVAS/GYzgX9wrF5MdA/i5aoJBsj/8wtm0WyDi6G+uEylZxvua8/eRcryOq+" +
        "pJ0d/EV8Vmv8veyMvyadobSXq9Y9K/TURmsFsn4KRX8lVWqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmql" +
        "VmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmql" +
        "VmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVmqlVur/V9STrKjPqkTnlXUMwLhup7oqP8emrnkPGJlzE5cpm0ZPQl2/dBOAB/WDKtNm" +
        "mbzuQXlv2/JfQJaosgnXnJH6OpCVL5UZcOd1psylEwD8FJui/ULKZkkTn0dDrCJcqb9XVx/YYdVZwW0m1GvP1oek5VCL42rvR0/Y" +
        "jayQutVU3+Os1JY/hM8t3c3hqqgD+HlSXi9jFFeehWqcpNfZcbUNH43SoN6+9bFc0Ushpx8r6s3SRVRJcatE5uLPvDUKoGhY7ZvB" +
        "O8CbVcXXPWA3g3Ti9OPSNR142chHzIXJuvTLl0vy4T6MW3A0BLbskEYukO3DXrmcBwDjjfRtw3hjaJsc38mBwSXopYxtHyOzhzjf" +
        "cEl5P8s4jB8yau367DrBuajrpPA+jDGQ5jAE/isGvpggB4ZfvunZB4FdzrObAHtZZmL2sgLGf2vYywvgLRgVkP8aUo9tApxvvXaF" +
        "9WtcXofkPdbWrlylk59X1pfrq2EaQ+cOPE7g/asgz9/iCHgv8OAXk3cP7Ex/8OE18B8b+N9XY/wP34bxZ13I4cNLMCjDgAibxjDg" +
        "Mh6OR8R+fj5ZF1Mrn3MpsRmF99Zhx4Miyuh0DbGfwk/eggQnBt6zqpW99SDh0vsvw2Er4XJ+1TA2hs6HhhS4lXHF5gAObAgD6Q5k" +
        "mLOFqAuoczrNB0HPRmPGnVjePUwjrmoEfD6QOCn4XmwdkvulRnfPAa0EIpxsUPr/zJxPQ8wCXTFVEGGu2xseEAWz0VrwyRYQ+HP+" +
        "M2imRpQIeBugDzCGq+mURSXwoJHn+nTq0Vx864O/ZkV7zHopN2fvPGg07TVjMSsAvzBwPbLhMbsTJXGAN83psncbttg2s/GOnBQC" +
        "HvPM2HaTK5VrSiZiKAyOnYTCANkatOCo6vcN+O5vnn/y3e7swoITlctnQxRLuC1/b/bOG38HfBIoWnZy6jneRj59GD0x9fi5OV3I" +
        "F+3t3NN3H9546vpufdmtNkX3pvYwmxSfXUJDjqPOk+PCvuYbreTULtamZycH5HMxZcrzodVBJ5msc6b/5Ochxwarf/ZuU2/SU88J" +
        "nDhu7ituczQl76Kpi+0ze76pclD9TSlTxJcC+5O/bFa8PTdL1d/aUKc2D+5RbcHvx9hE8k9wEHPms6f2jEpcX6DITd+fTvupBOCb" +
        "e8C/J7ZmHu1cHHVhnzpAi3yS7HY4U++5edUwLD6YSKDlAtdJgUFqx5bNu6tzynrPKniaTXa6s9bXNXPD9a2nkVre0USn0yp8Fwdg" +
        "B1ITXxi1/AZIPNgocLIhuMk+MH9y4832GQCkvwG/P6XpfwM7A3A8sH+L6LZd43a5OL32vwb5/i14cQiv7Ce09t9AMhg7EVks8DMA" +
        "Ntwdspg8eZ2UPMHQlcDuJ7x/McgQfoKBN2gdhNPjyk1uL995kl1Nncg+bGSrzy63ZURfJGmLJFc6IvQkQySmJ3EnCYWe3Jecoitx" +
        "R+I1bjiStNOeZNdFCnoiefs1yRDZeiSS9URIQpGuiDyS74ikEosUoYihJ9ITkdB+X6YcR52HIh/GIpJ1RNK+iMR9ERFJ6NaV7otI" +
        "Mdti4YgUfl+k6L4mIiJp1zZ3V0TkOxW1rdsvL3rl93NSH1t+kDepF5bCUrx2WmNPSt1qetLTy+cv9NeFJ9yozzE4jneiGfjOBUIX" +
        "F3li9pEv7nnObN3z9j6uYqVb8/vtpWT9YGlX6S88GD5fKWlzc1YNWTL0SqcqeueFrSJWPnO2ia6pw+UE5yw9i8tMWzqja96qrPH2" +
        "1CxunteFfOpp+ZDrv5zY0rFKdbgPIN84dUv5tKj/+NOTXcuxSpXmVmtPMa6W/4TUzgX/OvUMTt2VWqmVWqmVWqmVWqmVWqmVWqmV" +
        "WqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmVWqmV" +
        "+qmX/wNyyaYL8ySMEgAAAABJRU5ErkJggg==";


    internal const string FontData12 =

        "iVBORw0KGgoAAAANSUhEUgAABmMAAAJ4AQAAAACxe6oKAAAXfUlEQVR42u2dz28kV17AP6+6NC6k0bgWcfAhTNfCHjgswkIcstIk" +
        "rmSXJUhISPwDTCQOcGIWcZiFSfycRMkIoV2LU5CAGAlp98Ah110tpJxY7ByQ1htxhS17rZXhstXGQHVvdX051O+usseecTuT6Puk" +
        "cZe7X716n/f9/dqaZ4TPTjtw+Cw1pVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEa" +
        "pVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapVEapXk8zf5lhzl59mjm" +
        "YrsfBp3f5mDPGiZ5xmgkJHs9an1yVNK8BTANIYMIyM4aZsuWWLv1KgiIP6ne5xCmXtVzqTQzSDPg0SFwCvxtWPxXnq8DpCFM4S4w" +
        "P2sqNgJ2gPAYxACcWJj4AO+CMQEcz0DszrJpUvDc8h15IYRvxoSASMs21s8a5gjmd+ICVchgRtj+XB6WFz/+EeS3k+vyAhEwYx/x" +
        "7+UA+d1i4eEIUpifNVL25aQQak4OKTGQVHKc3SsftrdmyV69f50+bfqDFEixQLZdWXkEzwF2SNMiwN6HbYDbAsn9BIjrEaubHrqQ" +
        "2jvXQWND8IATHpGTZVCaLQbw4BZk3xkaxoMU4B0gezWC0dtp+dGGWPgYGMteAW6uMd7EcAQQhKbrey1Ie7076wCGDNaKXyyu6cSt" +
        "vZJZfAcSbi+dJuzqDeVq193EBw9unDwaGMaHhLBC7cSpEOAhEBBdZ/R0HQC30Lb6s2nps0oZmH8K+6N0/ltmA3CMLf01FCtRMJdh" +
        "Z9k0XqMzVcjv+tE8aCUI2wxkDXlLUlVE7mQVtf9Yvd6sc72UEsgJQKldbgS/fMZIcX+AZiUKuACSPLwmTRsZy+7kS9Xjophayfwm" +
        "W7uV9kfJw9ZIN2ph55UwsrCxxk+kInBit1C+RxQGkBWUWXDmWN1PtgeZ08xeN03asiR/u+XlnBg2hvKAvsMus+4iT6vFaWbTln0t" +
        "nWZj/E5EBhjs/sCK49oz4s1CfSABzAD2T6vPMwubf7LMAsIZCuuvAfxxaSvhrmlHn5FnzxJmx0efVGYS5ZBMgvLt8B+v026KyPHr" +
        "wO/d3QFIoujxVdlAruLbIgdlVYD945I5SGE8nE8sy25CCWFcyCCObVebzOPN+LD0AjHAemyJo6wst/1ZVQldC00ABOzXse9f2/Iw" +
        "jQM+b1tgD+ROWvqRyAfeqyw/EfDf27lO2ayVhrAF/FXibV52UA/MR1mRdBqI4G5URq5vYfEOw2ujsbASZUC++TcWCVK3X/+YrxX3" +
        "bTnQTuPaoca2cyYrpZt/N4pw2Vo2TTnnN3YnFlaLsvOXInJzedFKlbXtNBoaFFjCeszILN8L2MVacp6FMYQPzrnbdkuIVswv/WPa" +
        "rBNYyAOPJYbPc/c6TULGi/33V+SbeUeiXjczsGVds+vfbKfVfn2Hs2yaUqMebIxt6dlmqQWQczIrSRsZ5b3ok/dDWZnsLd1DO/0S" +
        "tHTZF0gT/U4VNhvuVBRIDm3luw5NK5butJzpd7r7Ao9vp1WFIUk2uEA8vC4aodjSrMm8xxe+EnSy0xiI+iNny6zTzvUCDnBcGvpp" +
        "y9L9i4z5CIgr2zgls4wxdWoa1NsES6SZdmquJiDETnbGreWG9E5t7m6nTnN7RYItM+p6i2F5NIUDq+ouF9j3Nn3cxJy1V1smAPdK" +
        "y5BKGhIQNvsCOdNSpEkxbrhEnXMWN9EaZSkphXHdobuotnLSWaGEZdTJd01MWlm6hwPYw9JxxxlwbK/LbixwzDaw4xLgTE84tI/z" +
        "qhbAbrfYMlkHEovwb+BFbqG9TpJa+PG1eoHpvAgN3w0x+PEZFY2pf6YA3nsBfDgNi9/msxDmwUPc8BVw918phG9Ofwh8Zfm7g2ET" +
        "wKPfiEBC89sW7GjNGXaycZP+A9zo5vk/Z0sz3F0HZ+KXj3rtqzB5fmkuWgZaOhKRfFNEROLVfEVERPKxSLrZ6vVRfTUREZHOUOlK" +
        "eZEjImKrG+OxSCJLarEZDO/76wPYn4+ZrrQ9Rb3ER8/xTLRLnOxnFvdmIp65donc/N7C3oy3aEGfKprF3YxtFurWTxXN2Z4oerZp" +
        "1ofefK67myFNXnD/Uygbv53wPJtewFz87NXZDYKYZ7ld5uzVUe/7gk+zhzZDNfGnVtOe/aan/CqN0iiN0iiN0iiN0iiN0iiN0iiN" +
        "0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN" +
        "0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iiN0iyxSesw1cvQbDeXUzPY" +
        "Y2IuM4/2oVVbzRmO88E5nexyyvd9gImF9pmJ0YVptgAmzbGKB8YAPCJgbowD7BrjMS2utwnAOGwZ5sbjwIQYh62QLVMOI8ZibIG9" +
        "IxZ2jQHEzoBikFMJmbpshWIctjx2DQcuPw1J+C7MQxJ4A34KWy4+7XPTHkMjNgS2i0OYy9M9Q+Cl9Kj4uNv7zeToIkIpFlPeiXeq" +
        "AWfjTVsNmCT7MO8cxhs3l0cxIJvF6Zxzk8Jqq+f5Z7NmjEXyUbIhIrIiIrlJxiL5WEYi0YYdi2SjbEXEbhqRdEOMSDKWdFUk2pR8" +
        "JJKM5U2RdFVExIhkKyLJWGRVJN3IVkSyUbIqEksyFklWow2R7+erkgIbkmxIuinpqmRjyVbkQNJNiVblIxE2ZVfEwqrkq81Jso+R" +
        "TXY/gfzOrXpxzI2bCczuEkEQ3U/AuTl6CC/ZQ0gjIvACXB8Ci3HBC/hLcH0oDmr2imNOtyGNRhk4N2+m8GNuJuCtr8fwyICRN3Mg" +
        "xLW44AQ4HjEu3IE9iMGBVcku5dPM2ymcRuYPoToH3EkhtcWRhTdOa93Dh8ngSYbNqetSjpEULgNer6awh1Mc4wxsYxeHyFsTTvHA" +
        "j9gDn8y/lIc2GUzgTjGX2ndVn3afezh47Hz/1PW06r1RPeIhJgNwE2QoiITFYmQWk2FrCZMGl6GZlY8NmhUyxTtFe7HTe29wvM4h" +
        "1Gkj5L1GkuLTE4jp3mPS0nlY8cGNi3iRhJeLnhb24BYdT1h7+HGn78PBEdJ6nn5xZfLuG7ZZGAfyYEHABjLb1rqgVlZi2zl5+DE0" +
        "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" +
        "URqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURql" +
        "URqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqlURqluWaaXe9ZmlZKcexvdKWy" +
        "2W4up7a8qE5r22v1C+ofV9P88vULT0NTnyE8XyAAHlYXSdklBJjB3BaHZB9cHYxUx/LdekKaF6F1KmzWCLxqNVjc7vJ/kEXL0zh5" +
        "QhoD8M+1WhUvq62eSXXxH8XLioXiWPmY20Dx42paXimtZy9138VP9qu9xN+XsgifdQ9dn69+cmXC77fT66JZs13bGNSCjkx8i3tJ" +
        "X7Z9TTRPsOyrMIqeQU3zgC9UEztngtnTPtK9Jtnk1/HE6/MCL8eLweVTTJMZPt2tQ5PaMkSKB2z1e0/L/Ek6mtg3skJld8OeY9kN" +
        "q1sWboqWoGmVhpl7INYCW7bMEw584LhMdWZ5NbUpzC0wLziPdsMt4OggAAn34WTiA1N3BwIPwv0ix7Nzy8RMHebG7kw8JnfnAVve" +
        "rs/EmCuimdSJmYWcCLARTAlhJwV+/ADSLZj+yABRDF6ZS2c+wDyIYhsiQZxAfvsU/OQUOJ5bDuYgt1P4us8ee+DhFh4yyYq8KXzq" +
        "LHzBebUi4/8kvwZz9uE/k99F3gP4nY9hHzhZA7CrjZKkpf+wt4jJiVLIXnUQ4jnwrXgDMsg//0dVeg43VkbfYGVk/Z8+x600i1l/" +
        "/rl7GPnwimRz2A4mzk0guw/85GbI7G4EUp3HfeQakDspJHC/ECvA6IM7/3sfRtHfZZDaEBP/wFp4dy3gdgqEIRnwPM+DAb/Q7QDD" +
        "tMwEHa6Kxu2I2klg+nYKkQOpXYc88C3Pb0LEbcg/yhrx/qQ0/4/mbycwfvV1mDAG//17ERK4wH9HZMDKOwluqRO2LjsyByCzwFtX" +
        "RBO14ieuAW6Ysk4ztCvth0UN0YpKj6q6ohjQhtVsQ69K7W72rSIo0qkQXk/AjZ82q3W6Sf9qrwJ40KpusrB9mzOQSjq8ZapiKcYH" +
        "rBtX+rsG/ErrngeFpWY5EB6VRZaL135QBAftNb4EjR0uavzGZ3rRsM+YB/1VdxaiiA9E/mKWm+cWC+NHpdJ5ub/gpOLwZ09E4w/W" +
        "egKWw8XCeiEVHYh+USsqV1f77Q4vFssRZqVwncQBsqC1H+HEEO1Pn4gmLsvjMm73K8yd43N2jC4W0Xqmk+UG8INKn9MQPq7r+3vw" +
        "jZOPn34HSl7qv3dQKlSaL2hWYs8tvoedJrkFMXVXg1sS/Wo9iffhc5vh09PMhnYtTMTgpoAfXfBpbeGOi5UZp10J29bmRg4S3HWf" +
        "niYdqKY3S+/yIFuQhRdf6GnGdmq9OeA7xRzi6sdB2+ayEMLn1p6eZnLOXS8s2qWbnN25rVz38u6eAmD9OgQ88IAPe/nWKH56mrj2" +
        "kUmnLAUIfn5xmAE3kDcvtrWN0FbmYgvnT4GfpZVI3urJ84krgv4n0ZDTXbu5+Mjh3YKwZ2LRedtSof+UG1yP/cbDa5loff2znmK9" +
        "PoTSE9isVrsM8PYAL/Z2gPhBJU1PlkJTjWpSYEwboG8lG2dEXcjsQgbRqHFSZHge8OhFINiB3M+XQpN39vIsVVpI67uExpLO1G7T" +
        "2bZyWs7YbVn3tpTrlwVL0bR8DqQSFNmhH30bOJZi0t/r9R4IOIGdAHYGbnzcEkoGJKeVFru+C0itgmm4FBr5LyAjKvIp7wfb4Mxm" +
        "Bc3c+IvmtT8AeAhEGXD8D5XyucAOTvrt1rN9IPAt+CEkNlsGTfDFCcz/HBi/FuKe3H2IEQ8ePiw0Zf6CnVt8H+Dw0D3l8JD5C1/6" +
        "up0g41BCvP09YP99cKbvwuGBhY1XYL4D0+29zhROwkqlY+Mtg8b3gfW/APy3LA73ATsqbCAkIyu2kQq/JY5bqH1MhM2JiXEn7wKT" +
        "HTDcLdXow3UIwRSK5beCq8elv60ZUqiqrUq3pRsi8mBVRLKRiGyJiMRlpwTTdMw2REQ+kF5jUySJRESioo9kiEi8KRKJiMhYRPKN" +
        "fKN+ZHdIEUk35RItbmhWLnHb9/MezRktuUCfuk2flubJ/l7gFXM1e5PL3VW/qHZe6bfpF6r5npTGmMfnef4ztLRXOsByW3o5L+cu" +
        "ZmWXT2Kz8KrmfnqztbnxVLK55H7p4VJFYi5SMl6ZpiXL+Sqx2TS4V77eeEKajfRSSxedu6n4ZC2JejRr1yGbtPkbovqvWK7A5Lc/" +
        "GZ/2Z9P1JbjFafDJ0Py+1/Jjv3BuZCpC01t7Fxg1Dj8ZmrWRPWdHc6Aav1ALntoAjfDZaQf6N7dKozRKozRKozRKozRKozRKozRK" +
        "ozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRK" +
        "ozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozRKozQA/w+ZCvgAE5qo2wAAAABJRU5ErkJggg==";


    internal const string FontData14 =

        "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" +
        "IPy5+/z838FgFvy46Upawiwa8wQA3v53QPyJLQPE5NcsiD+JmA1CMKeVufIfL7Zg+vQBGb2mf3t68jvOn/yTPwXDcKU0SN/29Tde" +
        "VQIeHAc9VwiJge99q3pEu+FbOLz/lhVI57AjESnGbiPekB33KRuL5KPmgflG/bFwO+y4NVNcH5Cuiogk9Xa2KiJfyr2PpIcx7/l6" +
        "iTGbb7V3vD8jueHv5eKlauGBaod7z+VaR+e99VT0QTCSLOWrtrcN8DVlNDSeX7jQcJ1k/eslx48XnzMN+tsA7zvjsOfw54tnNMLb" +
        "+oehq1XlOx9r779bvSnjs8Wf525+eIxPeKgfKjweo9/4qc3XZhh9V+kHMZRRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVR" +
        "GZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVR" +
        "GZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZXxPRqvPZXjJeOoubHj9Rwlxr/x" +
        "nRw3J5uaxsak5zITEvbZy+C03H5XxpkHMCsvGgMYYwCmdhYyNcZYEGOMhxhjfOAtU8vUeLw2Yes/Y9kxEbATOH2aGpiZECScWZi4" +
        "89kldHMC7M0sU2PMgKnxODbMjMfxAEwETAJmz+A1BBZ8xBiz8K7qa7zSfCwiCe5d6t8XEbEwEpEEVqUAVt13QxHrNoANKViVlLGk" +
        "jKq/BeNqvlURidelQKRgJJLBukju7qiAoZtmJFLAhmSAkYJ1SY0I65INJWck65KsS2ZErAgjkXUpACPy+sp3sjdfHM9IRCyrc8Z0" +
        "kw0RseNkXSSWzIhItslQJNtkVSQfSrYhkoylGIrIUKq/xbpIsSoikq+KSDx2//2liCQb6aqI7Igdi2SruRGRdJysi+QbWcUosYhs" +
        "uJPWRYzIuuQjEURWReKRFBtSAMMW43XsMQHZkQsAPgHw19MU5HP7TSBkdWyBdUmAdYkhf8YKgMV4gEf1twgAZ6o5ENtK+yFJnlwA" +
        "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" +
        "LNOIexRULlLUbgeEjcBwaufZ93y55o+Vcn/Rsbf6OQOAldopNcplf5/TCLyqD3MSwGAGfwww+3xRJU8vDx6u7eK3BGqjxqOgMKlq" +
        "/zFccF7riX/OT7obRVkzzUmv6CGbegGH86VcOeEn1jUnM8hehyEYwpmFxHhrQPba/DpkCYA/5RxILJKDlMKTHCNQTIwHeWzcU6Gf" +
        "lvcQHc2eAl9Ui/RHuEetFrFHlvIRkjcznzUU/ZUTsVSGuw1kx8aES7W6iIikIxH3yP+4+uqHIiJSrIuIwFAkYyQWkcSdFsOo+mGC" +
        "AEMp3A73xDpjKDEbErPhHtezIXZDUmAs1sh6zlAyNtyPBVIYiaTuZwc5L9dFcowk6xIzFJGMoZu58bsE+b6723RDCsYiCesiCYu/" +
        "eWj1cxaaDa7RagKnyc+ahr9e7o8RGMbLqjCMe3LopTZA6UbXMHGjwtvdbBjjWnPmrqomvLxKb/6EY73/1x151w9AyjVdHhmjhW86" +
        "jvzD+tOXIiK2efXENC89nm+k5cb3K02b9NzC6+ZcDdxvpNzXCG449e7LW17wzYu60ze4rPB6vCHfu8PJBnSGocceF0T3w1jc403f" +
        "UEWyujt813KM7k/3bnj8r+Lp/TDm9v7keEMz+Ovnd/jL2uZM0/tDHNxQRTZXb8so3Yz3+ZvkG6tI6t3yilEn19r9IZobn/HEv+Ul" +
        "Dx5Edo2xcvNVubt8xAgf+jjW/y9AGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVR" +
        "GZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVR" +
        "GZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGZVRGR+d8fhm75R8+7WUY89bdWe2sTF/m+kfln8lWjr25O7vUkLgDBovAL8L" +
        "XT0r/75qfjl/t2ZF/qa5+xXALLx7xqKe85/ehnFjAEiwyChNMc6Wz49qAe6Vx57fg7bltbZ881ZytABZvVlqnEkaBw/jWnmq14mn" +
        "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" +
        "gg==";


    internal const string FontData16 =

        "iVBORw0KGgoAAAANSUhEUgAAB84AAAJ3AQAAAADdBUZ+AAAfFklEQVR42u2dzW/kaHrYf2QxKmYhdNE+BDpMurjwIcgpOk6QyRR3" +
        "bDh78MF/gaE/YQLkMAYGXW/PDnb7MIAbOTkXR94Fct5jEsBZaraz7oOBaJMcckiyVI8Ay0iApTpyQmlZfHJ4+U1W6aP10dP9vIcR" +
        "q0S+fH/v8/2QPXKE93SsXN7boeiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiK" +
        "ruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiKruiK" +
        "ruiKruiKruiKruj3jn7+/qI7669JKDZPevrtQ5fw1fCMlx1dMMBP+c27JfWVgVSAU57Ve+GDD8CRsftggOd4BuBkk9TFznwI8DQC" +
        "pNSjEM4dxwe4gJXHeQArD+AofBj0YyhIAEsrAK8vIAZg3/6IAQlx40vdwYVEABGwMofABWH/or80sHJ84HhlgP3jh0FPSpEABnjm" +
        "Aae/MgT2q6T2AEWEk9RXjI1XwEmWVKBnnAEnpABZS7K/a6qjo3Qf5E/+4sFs3W0cnJiVgVc7MQZA7LoByIEUSJxNE3+zlVY2kVIA" +
        "CVnvlNW2/RnAL7ZTuNjdNQ/r4VOAi3+cxPDCKxWh+Kj5fWbgM+CnGyd+4WSVFiUIEGd5PbsdZ7v14TMXyCI/fguCWxbvAIbE/g2o" +
        "/BP79RelE/sI+GxtYHwBGPLKe1FgYLLVD4lpXDk+CZzM3u7B0IOWk94qjyKA7Kn94JUmXoDkGyf2MXC2AzBxTAxfOY1LEcmAI4CF" +
        "5JR5Qor3QLZu6kQmsJ4KfDctRi8JgWLtQp+BUBo48IIICFjW0c6OX5Q7G0HI53DUMYf7VviAKq7VB6ZloeKXAW4GznKjYQYAk1IB" +
        "wjp49HaocpsPUVG4w+TVrQJ4Z2GNLXjlhuw9ezY+pwRQwAK2q4uT2nT2O6eV6mZv8jWDIHDPbi6p+Vq5vDQbUAWgndMrhqKuKtWj" +
        "KLkfqlBap2I1VUcORVjmen5M7QeHTOHQ55Uyx+Z4Vp/CVjC9tGa6a/Rw7QkNjnSltopG513n/otGU7KotwTzoFJvfXTitkxMBJC3" +
        "Oc9mmybu76Jj1sk0AZLSwvIHQQ9LzTz4p+5Rs/+B15GigSIAvASemzVOqdygsJplr528tjIaY2OAB6T3LfKB1GOv166oarkgM/2y" +
        "S55e1x1/3XUdVeYW1+7g7XBzeenjQr/deHnUuTRalwSbQZQKq6hh77fyWz7NB+9MgrenQWX1O68C/L/pbUqK493EMK3rOD5vpQ4G" +
        "KIqQy9ped4+++FG+MOUyPsqigDF9dIE/La7cmsvq7M0rdT3qZDaufB6NB/97Qe/636UNRamx7scf0ce9a9wmAmS3bmpFJOCeB5Ux" +
        "OPwr8/YofADwj36nDlIrxzG9sHUNGRnb3Wm0PAVz8rpOD8zrersfFj0C/l8EbG+XeCfpNQz71aiFy7/Yr0uBeZwB+1LfLVq1sqQH" +
        "RM+BiK+BSVoaZZJlXbvNgOmV73IEFM+P64xtb9eqgqncZkgEkckfGN0vOyemJYffZyR1+3BDk2bsLkVtLWY7h9XcxGUoLKN8nD0w" +
        "ugHYKcvL3JyGIH5etapuNF4AFFJVL7bxSdI4yp9wAuHhf/YeBL3rw6d19n54AsVvFdcoL56N7ubEMVXM3MItwLFl/CnAV3EOwekf" +
        "vBUevtS9c/7Z5zYff5N0y2YFe3G9fU5z6wSQYBfw3+get4DunPpfAE6SlT2pP4J8XSATxzsog95TJ+x1X4b9mF5l5qflCopwuwCP" +
        "x/7bIHV2AHhdBAIZ0aXabsaq/U6xHySdvMlrqpccF5iY+O3I4asUPQwMROGbrKop87OOL/Xq/0SO3HtO417ym+NKRZNLmkcy7E+Z" +
        "dilnLf7XTnBghk4xq06+T2P3BptQfPeXs3ZOH/mQEqSjlzuy+t24nQSO1zOzy7bMcO9Nmhb6eEh9+Q+tNfq8frRxIkc66t3xV68e" +
        "99sTvWIhnZX7Fj6Iwm92rt51E61kNL0LB/ctRiLq29CqgOfVefmVH/uXlt2Cf1aDzyVdmJF20KU2c0/oDu2HJDYCOcXLjnZcIQA7" +
        "nbBerPt1W8dT87DoYdoYvzTF+ZVXNWzShD3p7leeJWh03KkqvAdEX1Mzm6BtxZcW1m2nWUTApHnmUNpAPtTxB0ppXvejjl+tOiJI" +
        "LrHC1yPdiXrq3PQC/W7LztM29RcPg/6f1lQecQakhCdX8IarsNWkqUV43k1wKvM3EEJigMDgAvKQrxa0PhRBJTADGcFFR5WDkRof" +
        "8rQ1r9eoxLnfcQIZhH2j8YBi520Ibmn1INQjBT/nUK5QXGRNFG+F6P/h+ODGTc/uA4oIIrIIDqxq2V5V+DDocee7zDqlc5u8eysi" +
        "nppr7KDT6ljYmQ/LPUmg4MzAHEytL+IDJ9HDoNe3jRLgooqxbvpjcHFm1RluOpSPDJs0n0sA5Ge1NZzJnwKkKQi/BAJTW4CXFAHw" +
        "5+Zhbd3GHAl/x5qp88+/AgeCeFODyhskwx8XIaz8VVDty8oNgfPw1BA4vw/48Y+rV2ndk3wP5IcPVLR+t5uC/3XlpfZ2AcNeOOkl" +
        "aWM70Hi/uTfQ3rjbB/EOvqrN4499A6/u1cuBjI+ChYjIX5Yfk4nk0/J4KiLZsnN2PmtdORcRkczpzRhPm9NZiojYW9j5pyKSyH2O" +
        "fF0kdc63oGm1z3MmWcswspHKszum/YRv0Vwzkb6HmGfAnAdS+OuN9XZfhHw7xg3QP2LwVmP+bcG9Ban3fV3LpeXRu4v+mMHD1Oxb" +
        "KPRWS603Lta+Efj6Ebz4qFuiz8aP3+axWiv19Q8XhcHD1FPeJVtf//DcN4Mniu1ZZry7tu4N4/jxOyX1DZfErZadHR++J+hOYvtW" +
        "3/bh3OBdieefnk+//eSrm6Q0EX+H91Tq78ZY6T/dV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RF" +
        "V3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RF" +
        "V3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFV3RFf3fGgfveosdvJvVV9+PTDXOcO8HNlnjqmDXC" +
        "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" +
        "9zCUpyVUmkJ+6m8BE44cA/m5E+4AxbnjNNYXHXNSUuft7S2AKAUOHK9aYGzg15VLDY7589YF8b78HuULcQbCiwCQEuzAG+265ZWB" +
        "5xmQn5fpaTtyV+OpSGHfhpR59d1R+Y7stHwZdiqSM5UcRyQp35GNq9cjWdrXWO07ssvyjUoW3XdkJV2IwExSx569kJhFJimOZExF" +
        "sll9L3uB1PeCmZiZxOWrtdULs3Yd1WJ/JFLMxb4NG8+q92tT2zccf1vyk8Y3x7360/HLDzudgsNJyuLUZnyzoWE/gsm6ZNbflJz2" +
        "7rXT6F8Eu51+yu66txeiy7KqzhvQs0telR19bbqUzJpLFv2v0uXIeXl/Eb17mUnnju115tX9e1Jfs6DWdZ0FzOXuxyj6pa+lL27p" +
        "7gfrXg8OeUvHxYvbn9O9Zkf4gUa2uqWJZF1KE90HhrnBNYd3oJAd9My8rVKP171OcFvo9zJOb3LRvzbn5k7RT++BPL3JZsvepzy9" +
        "bfZOi/P4g7tHPz7+8AbojrhXeWfw8nGwqKvSjgyO70Hqq+/fREA4iXM794/GFf7D+7D14GaXfbB1O/7y6wd0czeMUpOX3KWt38c4" +
        "mr8l8XJ17+hvzVjpv19XdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVX" +
        "dEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVX" +
        "dEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEVXdEV/t9HF8VXqg3He/RjWR6dRdRR0zrCfXtzhsl/VN3hxF+grUx501aH157oP" +
        "R76rP62iO0T/cX30998c3ckA+LL51ZkZFXpLwHHvu2PgOTyvP9zZ8JrlmDeXup2tNdHlf/q6f9f/CXx2L3ZqbtfWfYCi+ZyWP6dx" +
        "54qsPpKPq6Pc/vgZSA6fA3Cnf3vZHzm6OboLdP68+6vLri2+Fz+Qc24vM74FqfcnvXTO/J0KbkUw5krWjKxvcQ443o2X8vqB43po" +
        "3sCVfAI8u/FSDh8WPb+O10j7X3wEfHrjpUQPi55FtYWL/8AKeI/3y65k4K2RvFNuLjU1T/GelS+Om76vlVvyzkTta6O7Tva+Sj1u" +
        "onkWvF/oXkvPE+DUb53lABwYgPN6X8ozDqKyvLW16kG5g4dHkXgAzw8igKcRcC52vqdRBK/PQziqAukKjuH5yoQvy4/AqeELJDpe" +
        "EXEe3l4LRKpRTEVkKj+X9jCOiMQZCxHJmIsI/lxEYkdE4vqMfMa0/GIiks/ELMsjM8toTsjtaXa+HG8h8jSZi4A9X2KRiYhhyVQS" +
        "5vkyZSksxJGCSc5CMpiIFDMRsUvNFnKzkffQZ3LQ/nUBIhKnzEUkYS6Sw1xEpvFSxCBSzE28kPwRE5EMkcSiYzfBzJikLER4xERE" +
        "0ieOSIadL4OFpE48l2IeL6XAkQxJJiLpVNKF5Ei2LBwRM5eZiJkLIuntobv9ui3sVDM2k09JgZ+kwFkBsNrZjREDXOxFEVB00wCZ" +
        "NgqcktQnnD5d2gz4BDgpiGyBeBHuGi5a1biPH+FOAGcLPkUCiEIOwJHcuxtbDzqlMPnSxMA3aQby1TbwX5wYyEMfu9aMkAjyJwJT" +
        "A4/tlu3Y6z/LPy5+kqSQ5U8EeEVkmJpv0hz4a8fAzKbvPpwAU8PjMqU0OJXb9dMihPC2U93ubEW/t2r2AP5qO4cimAAvCIEsIsGJ" +
        "BVITAJw/LQsQ29XKS93xzg/kqwDw7AkvCGOI/vt2AXyfPcAYyIxXlzBOryPkg5e1y+jcvxP0POzXqgHAT92iwnnGDpAaL2WrPGVu" +
        "wO1elkX19CbwywPAECTA/gSQoOxyhaS4MJ/UNuNmnANPIMGASx5BADG4txd03X7h6Hd7D34CRE5VVEr/+iN7gUfUZj81tZyi0Muq" +
        "E8S3Z0csmxrhe5vL4gDIy6aID5CGt4TuDVqO6/pSGZiqi3M0X9smtG29x833UfeEpkYI13dE8o5InMIBK4RKBMHVe0lXlbrBa5ei" +
        "W9W3y8F1Wa+B15FFK+sIq1uEUAR2pYbIdGoE4/QbXSkQcZxKCJiyd/Uctst40S07Dm7S43H7CrbWi6aGvcp/XdKxlKt0qUp/4IzP" +
        "57WtS45b+hKbzka7AFHEv31DdGfQdvJu0jwqdjcQD3Vk2PR4Un7znYwIiJ4BHhICPAXyZj3OCQgvb9AT7KI/BqeL/uImPZl8zU6t" +
        "q4OD8ZZ/SGxVO2xphbjA37QcxL+Hggu59YbYeTQmMyR8DaNWbs+Kr3C/9hZLL2CJdYsmaIVYN2t0b9Y69xfAQp4Et43+7MPx74+C" +
        "JvV9866d3+/BR6X1P81z0/jzAqDwAD/q+JU8ir+8fsjbHBvkB3/7D9b86rNqkdlQu/30uosY7GAABITHTWQsg0DuQzsMFQFk5vwG" +
        "rezNUr/4YPx7R6SyNpMOA7N3eaunc9HE9KJ7aAzAz3jiNMZhNSALeqsOwWHrBg9MNqNnYcv1j7uieORX7nX7enuD8BZ2nEtrK20y" +
        "11cr5we37ebSS9UoOPzxiFJct5H9uOcdArsTRWTXZ8mfOOuixneqB9u36eYGOtp3YNunX42culyfkzda1BK0059WIpu6LgD4P3kj" +
        "+yS6rsO6GXpSWdDX686YmE9Hvo3MBm8+toM74yHC2M06fgIQuXUydxeV24YRtqqQlkOem1EPcEk46d72uC83vzBWmUMPIBYASatk" +
        "7l7RvUpB015eK/ujHmBtYC8uO0e6N90DeBa1k7mLe0LPBrEo63wuRh3husCeXfGdOu9/A/A3Vs0kwADhl3fah99goUFiI3Z6hbR8" +
        "TWB3Oy4zbVt8FcxM0Qmm1tWH1oASm8xl94NuDns5dk7SseU1jPm6+iUPAYMPfjZa+7acuetWmhUkQPDyNjtzl6LHL60o9koldgte" +
        "AhxWrYF01KGtCexOYd1ELLvg5bXZFOQRFDmE8W/a0PhBT7Oy4Nb6spvnCX/1U4D93IB3RoIjy+dAcMYflqccOGM103I8BC3tq0fJ" +
        "RQSsVt2SQ/4vcPhfhxEhM35abUN6b7b+KgI4+V9W4BGYL/ZS8FetnOTMyiWuv8ghem5ddVbac3lCZE4BTs4NuHhxafVCZsD8NgSn" +
        "3x/mzWnLdyRh+R7qLYzug6feSMkckRdMRURgLhKTsZQcFpIxFUnBEYFlMpGUqcjEfppJzFwMHzIpWJQnSLL4uYjEdj7jJEtJmFZf" +
        "m6lIxtw+2BIRkankCxFx7POxbC4i5q4ePA0c+wTgO2XjgBB2HYBJLWF/vGQPdhnrbAUHfwBgbEt6q/7+4Pewj2C80tWbVuaHa3XT" +
        "C7ndsVHq9rtfxQsRkdQRkXwiIiJxfXJ7CrEPGUXkN+M7XbAUkThZiIhk82RZPfRciEg6F5Fysh+JiMjMSr0SU3NspS639qR1dqMJ" +
        "Fhanh75xxNVBsrzWrZa3i/6mkeIsdgzfzvGm6L+8x7cczduF/vVYR/ZuRn4PUj91nPfhnz+9qdS/vJ2OyVWGM3p4G+jF2/7GXDZ6" +
        "nN3Y1YwJaXbNpzjm26Gg6+c7u50M6e7Q25VL04BNzRujS3DTKY5G2o53MdoK+vGau9+nFt3jS8TtXt4/qWX9R2+O7siN3JzpN6qD" +
        "O0N/2TqemLENuV+pf9yLb8WdJTj7b5vblHv7NzK/fv6WoYfmoqPi+Z1l9Lf/z+DkzUZTdNsRXuEaqjrTzK9xJxZyqyN33jAQi9vt" +
        "vn55+SPPc3+alQeTawQI55ZThtWbKrwTP+583r4zW3962xM6wns6Vvp/KFF0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0" +
        "RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0" +
        "RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RVd0RX+T8f8BNXxEw1Zv+msAAAAASUVORK5CYII=";


    internal const string FontData18 =

        "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";


    internal const string FontData20 =

        "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" +
        "AAAASUVORK5CYII=";


    internal static string FontData(int fontsize)
    {
        switch (fontsize) {
        case 4: return FontData4;
        case 6: return FontData6;
        case 8: return FontData8;
        case 10: return FontData10;
        case 12: return FontData12;
        case 14: return FontData14;
        case 16: return FontData16;
        case 18: return FontData18;
        default: return FontData20;
        }
    }
}
