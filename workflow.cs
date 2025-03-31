using System.Runtime.InteropServices;
using ImSh = SixLabors.ImageSharp;
using static SixLabors.ImageSharp.ImageExtensions;

namespace Frontend;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct MyColor //Na mojej architekturze tożsame (nawet co do paddingu) z Rgba32
{
    [FieldOffset(0)]
    public byte r;
    [FieldOffset(1)]
    public byte g;
    [FieldOffset(2)]
    public byte b;
    [FieldOffset(3)]
    public byte a;

    // Pójdę za to do piekła, ale bardzo nie chcę tworzyć nowych obiektów typu Rgba32, skoro już
    // włożyłem wysiłek w znalezienie API pozwalającego wrappować istniejącą tablicę w obrazek.
    public static unsafe explicit operator Rgba32(MyColor color)
    {
        return *(Rgba32*)&color;
    }
};


public unsafe class Program
{
    [DllImport("ImageGenerator.dll", EntryPoint = "GenerateImage")]
    public static extern void GenerateImage([In, Out] Rgba32[] color, int width, int height, delegate*<float, bool> tryReportCallback);
    public static bool Report(float progress)
    {
        //Console.WriteLine(progress);
        return true;
    }
    static void Main(string[] args)
    {
        const int width = 1024;
        const int height = 1024;

        Rgba32[] array = new Rgba32[width * height];


        delegate*<float, bool> tryReportCallback = &Report;
        //GenerateImage(array, width, height, tryReportCallback);
        Console.WriteLine("1");
        //Rgba32 pixels = ConvertAll<MyColor, Rgba32>(array, )
        ImSh.Image<Rgba32> image = ImSh.Image.WrapMemory(new Memory<Rgba32>(array), width, height);

        for (int i = 0; i < width; i++)
        {
            int red = (255 * i) / width;
            for (int j = 0; j < height; j++)
            {
                int blue = (255 * j) / height;
                array[i * width + j].R = (byte)red;
                array[i * width + j].G = (byte)(127 - Math.Floor(126 * Math.Sin(Math.PI * 0.01 * i)));//(byte)((red * blue) / 255);
                array[i * width + j].B = (byte)blue;
                array[i * width + j].A = 255;
            }
        }
        //image.DangerousTryGetSinglePixelMemory(out Memory<ImSh::PixelFormats.Rgba32> memory);
        //var span = memory.Span;


        image.Save("Image_0.jpeg");
        image.Dispose();
    }
}

