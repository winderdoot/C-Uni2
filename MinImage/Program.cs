using System.Runtime.InteropServices;
using ImSh = SixLabors.ImageSharp;
using static SixLabors.ImageSharp.ImageExtensions;
using MinImage;

namespace Frontend;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

public class Program
{
    public static async Task Main(string[] args)
    {
        CommandRunner runner = CommandRunner.GetInstance();

        while (true)
        {
            string userInput = ConsoleManager.ReadLine();
            try
            {
                CommandInfo[] parsedCommand = CommandParser.Parse(userInput);
                await runner.RunCommand(parsedCommand);
            }
            catch (ParserException e)
            {
                ConsoleManager.LogError(e.Message);
                continue;
            }
            catch (RunnerException e)
            {
                ConsoleManager.LogError(e.Message);
            }

        }
    }
}

//public unsafe class Program
//{
//    [DllImport("ImageGenerator.dll", EntryPoint = "GenerateImage")]
//    public static extern void GenerateImage([In, Out] Rgba32[] color, int width, int height, delegate*<float, int, bool> tryReportCallback, int callID);
//    public static bool Report(float progress, int callID)
//    {
//        //Console.WriteLine(progress);
//        //Console.WriteLine(callID);
//        //if (progress == 1)
//        //    Console.WriteLine(progress);
//        return true;
//    }
//    static void Main(string[] args)
//    {
//        const int width = 1024;
//        const int height = 1024;

//        Rgba32[] array = new Rgba32[width * height];//NativeMemory.Alloc(width*height, (UIntPtr)sizeof(Rgba32));

        
//        delegate*<float, int, bool> tryReportCallback = &Report;

//        fixed (Rgba32* p = array)
//        {
//            for (int i = 0; i < 1000; i++)
//            {
//                GenerateImage(array, width, height, tryReportCallback, 1);
//            }
//        }
//        Console.WriteLine("1");
//        //ImSh.Image<Rgba32> image = ImSh.Image.WrapMemory(new Memory<Rgba32>(array), width, height);

//        ImSh.Image<Rgba32> image = ImSh.Image.WrapMemory(new Memory<Rgba32>(array), width, height);

//        //for (int i = 0; i < width; i++)
//        //{
//        //    int red = (255 * i) / width;
//        //    for (int j = 0; j < height; j++)
//        //    {
//        //        int blue = (255 * j) / height;
//        //        array[i * width + j].R = (byte)red;
//        //        array[i * width + j].G = (byte)(127 - Math.Floor(126 * Math.Sin(Math.PI * 0.01 * i)));//(byte)((red * blue) / 255);
//        //        array[i * width + j].B = (byte)blue;
//        //        array[i * width + j].A = 255;
//        //    }
//        //}
//        //image.DangerousTryGetSinglePixelMemory(out Memory<ImSh::PixelFormats.Rgba32> memory);
//        //var span = memory.Span;


//        image.Save("Image_0.jpeg");
//        image.Dispose();

//        //NativeMemory.Free(array);
//    }
//}

