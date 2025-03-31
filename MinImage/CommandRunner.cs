using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;
using static SixLabors.ImageSharp.ImageExtensions;
using ImSh = SixLabors.ImageSharp;

namespace MinImage;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;
using CommandType = CommandInfo.CommandType;

[StructLayout(LayoutKind.Explicit)] // Just to be safe. I don't trust anyone, not the compiler, not the documentation, and certainly not myself.
public struct Circle
{
    [FieldOffset(0)]
    public float x;
    [FieldOffset(4)]
    public float y;
    [FieldOffset(8)]
    public float r;
}

/// <summary>
/// This class is meant to encapsulate all unsafe opareations that take place in this codebase.
/// The compiler scolded me that I cannot do unsafe things directly in asynchronous functions, so instead I created this class
/// to call it's static methods in asynchornous methods elsewhere. After many iritating moments I decided to make into a singleton.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>
/// Progress report of C++ callbacks is organised by call ID's - unique int values that the user of this class has to provide for each subsequent call of
/// a C++ function. The user is expected to provide reasonable ID values and reuse old call ID's that already expired. In practice, in the <see cref="CommandRunner"/> class,
/// call ID's range between 0 and the current number or running routines.
/// </item>
/// <item>
/// The user of this class is also responsible of setting the <see cref="_routineCount"/> to the appropriate value before making any c++ callbacks, as otherwise the class
/// will not report on finished routines properly. The user is also expected to not change the <see cref="_routineCount"/>, before all routines finish.
/// </item>
/// <item>
/// While individual C++ callbacks are synchronized and are meant to be called concurrently, only one caller is allowed to be using this class at a time.
/// This is guaranteed as long as no two threads attempt to call the <see cref="GetInstance(int)"/> method concurrently.
/// This never happens in this code base so I didn't bother guarding against it.
/// </item>
/// </list>
/// </remarks>
public unsafe class ProcessingLibrary
{
    /// <summary>
    /// Dictionary: int callId -> ((int indexOfCurrentStage), float[number] arrayOfProgressForEachStage)
    /// If this was a simple array we could do without the concurrent thing, but in testing it turned out that even if all tasks inherently posses different all id's, concurrently adding
    /// can still break. Whatever.
    /// </summary>
    private readonly ConcurrentDictionary<int, (int, float[])> _commandProgress; 
    //private readonly Dictionary<int, object> _progressLocks;
    private readonly object _finishedLock;
    /// <summary>
    /// The <i>readers</i> of this lock are the routine Tasks attempting to write their progress report into the <see cref="_commandProgress"/> dictionary. 
    /// If the <see cref="CommandRunner"/> thread is currently <i>writing</i> that is updating it's own progress table and writing it onto the console, 
    /// then the routine task just skip it and keep doing their work to not slow down the apllication.
    /// The <see cref="CommandRunner"/> thread periodically acquires the <i>writer</i> lock to read the progress and display it on the screen.
    /// </summary>
    /// <remarks>
    /// This was previously implemented by an array of lock objects and it sucked tremendously. Believe me.
    /// </remarks>
    private readonly ReaderWriterLockSlim _progressLock;
    private bool _writerLockAcquired; // this isn't actually atomic and perfectly accurate, this flag is just for my own convenience
    private int _finishedRoutines;
    private int _routineCount;
    private int _numStages;
    private CancellationTokenSource _tokenSource;
    //private ManualResetEventSlim _newProgressMade;
    private Barrier _routineBarrier;
    private ProcessingLibrary(int routineCount, int numStages)
    {
        _commandProgress = new();
        //_progressLocks = new();
        _finishedLock = new object();
        //_newProgressMade = new(false);

        _routineCount = routineCount;
        _numStages = numStages;
        _finishedRoutines = 0;
        _tokenSource = new CancellationTokenSource();
        _routineBarrier = new Barrier(routineCount + 1);
        _progressLock = new ReaderWriterLockSlim();
        _writerLockAcquired = false;
    }

    private static ProcessingLibrary? _instance = null;

    public static ProcessingLibrary GetInstance(int routineCount, int numStages)
    {
        if (_instance == null)
        {
            _instance = new ProcessingLibrary(routineCount, numStages);
        }

        if (!_instance._tokenSource.TryReset()) // The instance had been used previously and need's to be adjusted
        {
            _instance._routineCount = routineCount;
            _instance._numStages = numStages;
            _instance._finishedRoutines = 0;
            _instance._tokenSource = new CancellationTokenSource();
            _instance._routineBarrier = new Barrier(routineCount + 1); // The extra participant is the caller thread (CommandRunner) that manages displaying progress 
            _instance._writerLockAcquired = false;
        }
            
        return _instance;
    }

    public static float ProgressSensitivity = 0.02f; // I know that the c++ library reports every 1% but I don't care.
    public void WaitForRoutines()
    {
        _routineBarrier.SignalAndWait();
    }
    public void GetProgressAccess()
    {
        _progressLock.EnterWriteLock(); // Blocks
        _writerLockAcquired = true; // this isn't actually atomic and perfectly accurate, this flag is just for my own convenience
    }

    /// <summary>
    /// This is weird and only used by the <see cref="ConsoleManager"/>.
    /// </summary>
    public void GetProgressTable(float[,] progressTable, int callID)
    {
        if (_writerLockAcquired)
        {
            int stages = _commandProgress[callID].Item2.Length; // Assume it's the same, whatever
            for (int i = 0; i < stages; i++)
            {
                progressTable[callID, i] = _commandProgress[callID].Item2[i];
            }
        }
    }
    public void ReleaseProgressAccess()
    {
        _progressLock.ExitWriteLock();
        _writerLockAcquired = false;
    }
    public CancellationToken GetCancellationToken()
    {
        return _tokenSource.Token;
    }
    
    private void UpdateProgress(float progress, int callID)
    {
        int stageInd = _instance!._commandProgress[callID].Item1;
        if (stageInd >= _instance!._commandProgress[callID].Item2.Length)
            return;
        _instance!._commandProgress[callID].Item2[stageInd] = progress;
        if (progress == 1.0f)
        {
            _instance!._commandProgress[callID] = (stageInd + 1, _instance!._commandProgress[callID].Item2);
        }
    }
    public static bool TryReportCallback(float progress, int callID)
    {
        //Console.WriteLine("report");
        if (progress == 1.0f)
        {
            _instance!._progressLock.EnterReadLock(); // The routine has to wait
            _instance!.UpdateProgress(progress, callID);
            _instance!._progressLock.ExitReadLock();
            if (_instance!._commandProgress[callID].Item1 != _instance!._numStages) // Check if it's the last stage and if not, just exit
            {
                return false;
            } // Now it was the final stage and we can mark this routine as finished
            lock (_instance!._finishedLock) // Also, trust me bro it's not null, the only way to call this is thorugh an instance
            {
                _instance!._finishedRoutines++;
                if (_instance!._finishedRoutines == _instance!._routineCount)
                {
                    _instance!._tokenSource.Cancel();
                    return false;
                }
            }
            return false;
        }
        //Console.WriteLine("locking");
        if (_instance!._progressLock.TryEnterReadLock(millisecondsTimeout: 0))
        {
            //Console.WriteLine("Good progress");
            _instance!.UpdateProgress(progress, callID);
            _instance!._progressLock.ExitReadLock();
        }
        return true;
    }

    [DllImport("ImageGenerator.dll", EntryPoint = "GenerateImage", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GenerateImage([In, Out]Rgba32[] color, int width, int height, delegate*<float, int, bool> tryReportCallback, int callID);
    public void TrackGenerateImage(Rgba32[] color, int width, int height, int callID, int numStages)
    {
        _commandProgress[callID] = (0, new float[numStages]);
        _routineBarrier.SignalAndWait();
        GenerateImage(color, width, height, &TryReportCallback, callID);
        return;
    }

    public void TrackInput(int callID, int numStages) // This is only for synchronization and unified calling semantics
    {
        _commandProgress[callID] = (0, new float[numStages]);
        _routineBarrier.SignalAndWait();
        TryReportCallback(1.0f, callID); // The images were already loaded beforehand so sadly no progress visualization
        return;
    }

    [DllImport("ImageGenerator.dll", EntryPoint = "Blur", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Blur([In, Out] Rgba32[] color, int width, int height, int w, int h, delegate*<float, int, bool> tryReportCallback, int callID);
    public void TrackBlur(Rgba32[] color, int width, int height, int w, int h, int callID)
    {
        Blur(color, width, height, w, h, &TryReportCallback, callID);
        return;
    }

    [DllImport("ImageGenerator.dll", EntryPoint = "ColorCorrection", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ColorCorrection([In, Out] Rgba32[] color, int width, int height, float red, float green, float blue, delegate*<float, int, bool> tryReportCallback, int callID);
    // These Track wrappers are a bit reduntant in the case of processing methods as there is no need to syncronize nor initialize the progress dictionary.
    // I'm keeping them for consistency and encapsulating the TryReportCallback method from the outside world
    public void TrackColorCorrection(Rgba32[] color, int width, int height, float red, float green, float blue, int callID)
    {
        ColorCorrection(color, width, height, red, green, blue, &TryReportCallback, callID);
        return;
    }

    [DllImport("ImageGenerator.dll", EntryPoint = "GammaCorrection", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GammaCorrection([In, Out] Rgba32[] color, int width, int height, float gamma, delegate*<float, int, bool> tryReportCallback, int callID);
    public void TrackGammaCorrection(Rgba32[] color, int width, int height, float gamma, int callID)
    {
        GammaCorrection(color, width, height, gamma, &TryReportCallback, callID);
        return;
    }

    // Unfortunately I'm going to have to use a delegate instead of delegate*, because I can't capture state (closure object) in a delegate*.
    private delegate Rgba32 GetColorCopying(float x, float y, Rgba32 color);
    [DllImport("ImageGenerator.dll", EntryPoint = "ProcessPixels_Custom", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ProcessPixels_Custom([In, Out] Rgba32[] color, int width, int height, IntPtr getColor, delegate*<float, int, bool> tryReportCallback, int callID);
    public void TrackRoom(Rgba32[] color, int width, int height, float x1, float y1, float x2, float y2, int callID)
    {
        GetColorCopying _tmpDelegate = (float x, float y, Rgba32 copiedColor) => RoomGetColor(x1, y1, x2, y2, x, y, copiedColor);
        ProcessPixels_Custom(color, width, height, Marshal.GetFunctionPointerForDelegate(_tmpDelegate), &TryReportCallback, callID);
        return;
    }

    private Rgba32 RoomGetColor(float x1, float y1, float x2, float y2, float x, float y, Rgba32 copiedColor)
    {
        if (x > x1 && x < x2 && y > y1 && y < y2)
        {
            copiedColor.R = 0;
            copiedColor.G = 0;
            copiedColor.B = 0;
            copiedColor.A = 255;
        }
        return copiedColor;
    }


    [DllImport("ImageGenerator.dll", EntryPoint = "DrawCircles", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DrawCircles([In, Out] Rgba32[] color, int width, int height, [In, Out]Circle[] circles, int circleCount, delegate*<float, int, bool> tryReportCallback, int callID);
    public void TrackRandomCircles(Rgba32[] color, int width, int height, int circleCount, float r, int callID)
    {
        Random rand = new Random(DateTime.Now.Millisecond + (Task.CurrentId ?? 0));
        Circle[] circles = new Circle[circleCount];
        for (int i = 0; i < circleCount; i++)
        {
            circles[i].r = r;
            circles[i].x = rand.NextSingle();
            circles[i].y = rand.NextSingle();
        }
        fixed (Circle *ptr = circles)
        {
            DrawCircles(color, width, height, circles, circleCount, &TryReportCallback, callID);
        }
        return;
    }

    private delegate Rgba32 GetColor(float x, float y);
    [DllImport("ImageGenerator.dll", EntryPoint = "ProcessPixels_Custom", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GenerateImage_Custom([In, Out] Rgba32[] color, int width, int height, IntPtr getColor, delegate*<float, int, bool> tryReportCallback, int callID);
    public void TrackNoisy(Rgba32[] color, int width, int height, int numPivots, int callID, int numStages)
    {
        Random rand = new Random(DateTime.Now.Millisecond + (Task.CurrentId ?? 0));
        float[,] pivots = new float[numPivots, 2];
        for (int i = 0; i < numPivots; i++)
        {
            pivots[i, 0] = rand.NextSingle(); //x
            pivots[i, 1] = rand.NextSingle(); //y
        }
        GetColor _tmpDelegate = (float x, float y) => NoisyGetColor(pivots, numPivots, x, y);

        _commandProgress[callID] = (0, new float[numStages]);
        _routineBarrier.SignalAndWait();
        GenerateImage_Custom(color, width, height, Marshal.GetFunctionPointerForDelegate(_tmpDelegate), &TryReportCallback, callID);
        return;
    }

    // Modified sigmoid function

    private static float sharpness = 15.0f;
    private static float a = 3.0f;
    private static float distMap(float x)
    {
        return -1.0f/(a * (x-1)) - 1.0f/a;// -1.0f / (1.0f + (float)Math.Exp(-sharpness * (x - 0.5f))) + 1;
    }

    private static float distMap1(float x)
    {
        return  -1.0f / (1.0f + (float)Math.Exp(-sharpness * (x - 0.5f))) + 1;
    }

    private Rgba32 NoisyGetColor(float[,] pivots, int numPivots, float x, float y)
    {
        Rgba32 color = new Rgba32();
        float[] dists = new float[numPivots];
        for (int i = 0; i < numPivots; i++)
        {
            float xd = x - pivots[i, 0];
            float yd = y - pivots[i, 1];
            dists[i] = (float)Math.Sqrt(xd * xd + yd * yd);
        }
        float minDist = dists.Min();
        float avDist = dists.Average();
        color.R = color.G = color.B = (byte)Math.Ceiling(distMap(avDist - minDist) * 255); // /Math.Max(Math.Log10((double)numPivots/10), 1)
        color.A = 255;
        return color;
    }

    public void TrackWosy(Rgba32[] color, int width, int height, int numPivots, int callID, int numStages)
    {
        Random rand = new Random(DateTime.Now.Millisecond + (Task.CurrentId ?? 0));
        float[,] pivots = new float[numPivots, 2];
        for (int i = 0; i < numPivots; i++)
        {
            pivots[i, 0] = rand.NextSingle(); //x
            pivots[i, 1] = rand.NextSingle(); //y
        }
        GetColor _tmpDelegate = (float x, float y) => WosyGetColor(pivots, numPivots, x, y);

        _commandProgress[callID] = (0, new float[numStages]);
        _routineBarrier.SignalAndWait();
        GenerateImage_Custom(color, width, height, Marshal.GetFunctionPointerForDelegate(_tmpDelegate), &TryReportCallback, callID);
        return;
    }

    private Rgba32 WosyGetColor(float[,] pivots, int numPivots, float x, float y)
    {
        Rgba32 color = new Rgba32();
        float[] dists = new float[numPivots];
        for (int i = 0; i < numPivots; i++)
        {
            float xd = x - pivots[i, 0];
            float yd = y - pivots[i, 1];
            dists[i] = (float)Math.Sqrt(xd * xd + yd * yd);
        }
        float minDist = dists.Min();
        float avDist = dists.Average();
        color.R = (byte)Math.Ceiling(distMap(avDist - minDist) * 255 / Math.Max(Math.Log10((double)numPivots / 10), 1)); // /Math.Max(Math.Log10((double)numPivots/10), 1)
        color.G = (byte)Math.Ceiling(distMap1(minDist / avDist) * 255 / Math.Max(Math.Log10((double)numPivots / 10), 1));
        color.B = (byte)Math.Ceiling(distMap(distMap1(minDist * avDist)) * 255 / Math.Max(Math.Log10((double)numPivots / 10), 1)); // Crazy numbers
        color.A = 255;
        return color;
    }

    public void TrackDirsey(Rgba32[] color, int width, int height, int numPivots, int callID, int numStages)
    {
        Random rand = new Random(DateTime.Now.Millisecond + (Task.CurrentId ?? 0));
        float[,] pivots = new float[numPivots, 6];
        for (int i = 0; i < numPivots; i++)
        {
            pivots[i, 0] = rand.NextSingle(); //x
            pivots[i, 1] = rand.NextSingle(); //y
            pivots[i, 2] = rand.NextSingle(); //x vector component
            pivots[i, 3] = rand.NextSingle(); //y vector component
            float norm = (float)Math.Sqrt(pivots[i, 2] * pivots[i, 2] + pivots[i, 3] * pivots[i, 3]);
            pivots[i, 2] /= norm;
            pivots[i, 3] /= norm;

            pivots[i, 4] = rand.NextSingle(); //x vector component
            pivots[i, 5] = rand.NextSingle(); //y vector component
            norm = (float)Math.Sqrt(pivots[i, 4] * pivots[i, 4] + pivots[i, 5] * pivots[i, 5]);
            pivots[i, 4] /= norm;
            pivots[i, 5] /= norm;
        }
        GetColor _tmpDelegate = (float x, float y) => DirseyGetColor(pivots, numPivots, x, y);

        _commandProgress[callID] = (0, new float[numStages]);
        _routineBarrier.SignalAndWait();
        GenerateImage_Custom(color, width, height, Marshal.GetFunctionPointerForDelegate(_tmpDelegate), &TryReportCallback, callID);
        return;
    }

    private Rgba32 DirseyGetColor(float[,] pivots, int numPivots, float x, float y)
    {
        Rgba32 color = new Rgba32();
        float[] dists = new float[numPivots];
        float[] products = new float[numPivots];
        float[] products1 = new float[numPivots];

        for (int i = 0; i < numPivots; i++)
        {
            float xd = x - pivots[i, 0];
            float yd = y - pivots[i, 1];
            dists[i] = (float)Math.Sqrt(xd * xd + yd * yd);
            products[i] = (xd * pivots[i, 2] + yd * pivots[i, 3]); //Vector dot product
            products1[i] = (xd * pivots[i, 4] + yd * pivots[i, 5]);
        }
        int ind = 0;
        for (int i = 1; i < numPivots; i++)
        {
            if (dists[i] < dists[ind])
            {
                ind = i;
            }
        }
        float ang = products[ind];
        float ang1 = products1[ind];
        float minDist = dists[ind];
        float avDist = dists.Average();
        color.R = (byte)Math.Ceiling(distMap(0.5f*ang + 0.5f) * 255); // /Math.Max(Math.Log10((double)numPivots/10), 1)
        color.G = (byte)Math.Ceiling(distMap1(0.5f * ang1 + 0.5f) * 200);
        color.B = (byte)Math.Ceiling(distMap(avDist - minDist) * 255);
        color.A = 255;
        return color;
    }

    public void TrackNoicross(Rgba32[] color, int width, int height, int numPivots, bool mono, int callID)
    {
        Random rand = new Random(DateTime.Now.Millisecond + (Task.CurrentId ?? 0));
        float[,] pivots = new float[numPivots, 6];
        for (int i = 0; i < numPivots; i++)
        {
            pivots[i, 0] = rand.NextSingle(); //x
            pivots[i, 1] = rand.NextSingle(); //y
            pivots[i, 2] = rand.NextSingle(); //x vector component
            pivots[i, 3] = rand.NextSingle(); //y vector component
            pivots[i, 4] = rand.NextSingle(); //z vector component

            float norm = (float)Math.Sqrt(pivots[i, 2] * pivots[i, 2] + pivots[i, 3] * pivots[i, 3] + pivots[i, 4] * pivots[i, 4]);
            pivots[i, 2] /= norm;
            pivots[i, 3] /= norm;
            pivots[i, 4] /= norm;
        }
        GetColorCopying _tmpDelegate = (float x, float y, Rgba32 copiedColor) => NoicrossGetColor(pivots, numPivots, mono, x, y, copiedColor);
        ProcessPixels_Custom(color, width, height, Marshal.GetFunctionPointerForDelegate(_tmpDelegate), &TryReportCallback, callID);
        return;
    }

    private Rgba32 NoicrossGetColor(float[,] pivots, int numPivots, bool mono, float x, float y, Rgba32 copiedColor)
    {
        float[] dists = new float[numPivots];

        for (int i = 0; i < numPivots; i++)
        {
            float xd = x - pivots[i, 0];
            float yd = y - pivots[i, 1];
            dists[i] = (float)Math.Sqrt(xd * xd + yd * yd);
        }
        int ind = 0;
        for (int i = 1; i < numPivots; i++)
        {
            if (dists[i] < dists[ind])
            {
                ind = i;
            }
        }
        Vector3 p1 = new Vector3((float)copiedColor.R / 255, (float)copiedColor.G / 255, (float)copiedColor.B / 255);
        Vector3 p2 = new Vector3(pivots[ind, 2], pivots[ind, 3], pivots[ind, 4]);
        Vector3 crossProd = Vector3.Cross(p1, p2);
        //float minDist = dists[ind];
        //float avDist = dists.Average();
        copiedColor.R = (byte)Math.Ceiling(crossProd.X * 255);
        copiedColor.G = (byte)Math.Ceiling(crossProd.Y * 255);
        copiedColor.B = (byte)Math.Ceiling(crossProd.Z * 255);
        if (mono)
        {
            copiedColor.G = copiedColor.B = copiedColor.R;
        }
         // /Math.Max(Math.Log10((double)numPivots/10), 1)
        
        copiedColor.A = 255;
        return copiedColor;
    }

    public void TrackPointCross(Rgba32[] color, int width, int height, float px, float py, int callID)
    {
        GetColorCopying _tmpDelegate = (float x, float y, Rgba32 copiedColor) => PointCrossGetColor(px, py, x, y, copiedColor);
        ProcessPixels_Custom(color, width, height, Marshal.GetFunctionPointerForDelegate(_tmpDelegate), &TryReportCallback, callID);
        return;
    }

    private Rgba32 PointCrossGetColor(float px, float py, float x, float y, Rgba32 copiedColor)
    {
        float xd = x - px;
        float yd = y - px;
        //new Random(DateTime.Now.Millisecond + (Task.CurrentId ?? 0)).NextSingle()
        Vector3 p1 = new Vector3(xd, yd, (float)Math.Sqrt(xd*xd + yd*yd));
        Vector3 p2 = new Vector3((float)copiedColor.R / 255, (float)copiedColor.G / 255, (float)copiedColor.B / 255);

        Vector3 crossProd = Vector3.Cross(p1, p2);
        //float minDist = dists[ind];
        //float avDist = dists.Average();
        copiedColor.R = (byte)Math.Ceiling(crossProd.X * 255); // /Math.Max(Math.Log10((double)numPivots/10), 1)
        copiedColor.G = (byte)Math.Ceiling(crossProd.Y * 255);
        copiedColor.B = (byte)Math.Ceiling(crossProd.Z * 255);
        copiedColor.A = 255;
        return copiedColor;
    }
}


/// <summary>
/// While this class is a singleton, it is not thread safe. The user must guarantee that two instances will not try to be created at the same time.
/// </summary>
public class CommandRunner
{
    private static CommandRunner? _instance;
    private int _routinesCount;
    private Task[] routines;
    private CancellationToken _callerToken;


    private CommandRunner()
    {
        _routinesCount = 0;
        routines = [];
    }
    public static CommandRunner GetInstance()
    {
        if (_instance == null)
        {
            _instance = new CommandRunner();
        }
        return _instance;
    }

    /// <summary>
    /// Main method of this class, responsible for the logic of running parsed commands.
    /// <b>NOTE:</b> the method assumes that the user properly parsed the input command with the <see cref="CommandParser"/> class!
    /// </summary>
    /// <param name="commandChain">A chain of commands, that the user guarantees are properly pased by the <see cref="CommandParser"/> class</param>
    /// <returns>A task when the method finishes running.</returns>
    public async Task RunCommand(CommandInfo[] commandChain)
    {
        switch (commandChain[0].Type)
        {
            case CommandType.Neofetch:
                return;
            case CommandType.Music:
                await MusicPlayer.PlayMusic();
                return;
            case CommandType.Help:
                ConsoleManager.HelpCommand(commandChain[0].Arguments);
                return;
            case CommandType.Input:
                _routinesCount = commandChain[0].Arguments.Length;
                routines = new Task[_routinesCount];
                break;
            case CommandType.DirectionalNoise:
                goto case CommandType.Generate;
            case CommandType.Wosy:
                goto case CommandType.Generate;
            case CommandType.Noisy:
                goto case CommandType.Generate;
            case CommandType.Generate:
                _routinesCount = (int)commandChain[0].Arguments[0];
                routines = new Task[_routinesCount];
                break;
            default:
                throw new RunnerException($"Runtime error: Segmentation fault (core dumping not implemented). The Parser must have failed :(.");
        }


        ProcessingLibrary library = ProcessingLibrary.GetInstance(_routinesCount, commandChain.Length);
        _callerToken = library.GetCancellationToken();
        ConsoleManager.InitProgressReport(_routinesCount, commandChain.Length);
        for (int i = 0; i < _routinesCount; i++)
        {
            int callID = i;
            routines[i] = Task.Run(() => ProcessImage(library, callID, commandChain)); 
        }

        ConsoleManager.DisplayProgressReport();
        library.WaitForRoutines();

        while (!_callerToken.IsCancellationRequested)
        {
            library.GetProgressAccess(); //this acquires the writer lock
            ConsoleManager.UpdateProgressReport(library, _routinesCount);
            library.ReleaseProgressAccess();
            ConsoleManager.DisplayProgressReport();
            Thread.Sleep(20);
        }

        await Task.WhenAll(routines);
    }

    private unsafe void ProcessImage(ProcessingLibrary library, int callID, CommandInfo[] commandChain) //Not sure if it needs to be ref, just making sure
    {
        Rgba32[] imageData = [];
        int width = 0, height = 0;
        switch (commandChain[0].Type)
        {
            case CommandType.Wosy:
                goto case CommandType.Generate;
            case CommandType.Noisy:
                goto case CommandType.Generate;
            case CommandType.Generate:
                width = (int)commandChain[0].Arguments[^2];
                height = (int)commandChain[0].Arguments[^1];
                imageData = new Rgba32[width*height];
                //imageData = (Rgba32*)NativeMemory.Alloc((UIntPtr)((int)commandChain[0].Arguments[0] * (int)commandChain[0].Arguments[1]), (UIntPtr)sizeof(Rgba32));
                // NO THANK YOU, I'VE HAD ENOUGH OF HEAP CORRUPTION ERRORS
                break;
            case CommandType.Input:
                string filepath = $"{ConsoleManager.CWD}\\{(string)commandChain[0].Arguments[callID]}";
                ImSh.Image<Rgba32> importImage = ImSh.Image.Load<Rgba32>(filepath); // This should not throw because I checked it first in the parser class
                width = importImage.Width;
                height = importImage.Height;
                imageData = new Rgba32[width * height];
                importImage.DangerousTryGetSinglePixelMemory(out Memory<ImSh::PixelFormats.Rgba32> memory);
                var importSpan = memory.Span;
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        int index = i * height + j;
                        imageData[index].R = importSpan[index].R;
                        imageData[index].G = importSpan[index].G;
                        imageData[index].B = importSpan[index].B;
                        imageData[index].A = importSpan[index].A;
                    }
                    //ProcessingLibrary.TryReportCallback((float)i / width, callID); We can't raport, because the routines aren't even running
                }
                importImage.Dispose();
                break;
            default:
                
                break;
        }

        fixed (Rgba32* p = imageData)
        {
            for (int stage = 0; stage < commandChain.Length; stage++)
            {
                var c = commandChain[stage];
                switch (c.Type)
                {
                    case CommandType.NoisyCross:
                        library.TrackNoicross(imageData, width, height, (int)c.Arguments[0], (bool)c.Arguments[1], callID);
                        break;
                    case CommandType.PointCross:
                        library.TrackPointCross(imageData, width, height, (float)c.Arguments[0], (float)c.Arguments[0], callID);
                        break;
                    case CommandType.Filter3:
                        break;
                    case CommandType.Generate:
                        library.TrackGenerateImage(imageData, width, height, callID, commandChain.Length);
                        break;
                    case CommandType.Noisy:
                        library.TrackNoisy(imageData, width, height, (int)c.Arguments[1], callID, commandChain.Length);
                        break;
                    case CommandType.Wosy:
                        library.TrackWosy(imageData, width, height, (int)c.Arguments[1], callID, commandChain.Length);
                        break;
                    case CommandType.DirectionalNoise:
                        library.TrackDirsey(imageData, width, height, (int)c.Arguments[1], callID, commandChain.Length);
                        break;
                    case CommandType.Input:
                        library.TrackInput(callID, commandChain.Length);
                        break;
                    case CommandType.Output:
                        OutputCommand(imageData, (string)c.Arguments[0], width, height, callID);
                        break;
                    case CommandType.Blur:
                        library.TrackBlur(imageData, width, height, (int)c.Arguments[0], (int)c.Arguments[1], callID);
                        break;
                    case CommandType.ColorCorrection:
                        library.TrackColorCorrection(imageData, width, height, (float)c.Arguments[0], (float)c.Arguments[1], (float)c.Arguments[2], callID);
                        break;
                    case CommandType.GammaCorrection:
                        library.TrackGammaCorrection(imageData, width, height, (float)c.Arguments[0], callID);
                        break;
                    case CommandType.Room:
                        library.TrackRoom(imageData, width, height, (float)c.Arguments[0], (float)c.Arguments[1], (float)c.Arguments[2], (float)c.Arguments[3], callID);
                        break;
                    case CommandType.RandomCircles:
                        library.TrackRandomCircles(imageData, width, height, (int)c.Arguments[0], (float)c.Arguments[1], callID);
                        break;
                    default:
                        break;
                }
            }
        }
        
        //NativeMemory.Free(imageData); NO! DON'T PRETEND YOU'RE C!
    }

    private void OutputCommand(Rgba32[] data, string filepath, int width, int height, int callID)
    {
        filepath = filepath.Replace("/", "\\"); // To allow unix like paths to be written
        string[] filenameAndExt = filepath.Split('.', 2);
        // Defaulting to .jpg if there is no extension
        if (filenameAndExt.Length < 2)
        {
            filenameAndExt = [filenameAndExt[0], "jpg"];
        }
        
        filepath = $"{ConsoleManager.CWD}\\{filenameAndExt[0]}_{callID}.{filenameAndExt[1]}";
        string? dirpath = Path.GetDirectoryName(filepath);

        ProcessingLibrary.TryReportCallback(0.5f, callID); // Simulating progress so that the user is happy

        if (!string.IsNullOrEmpty(dirpath))
            Directory.CreateDirectory(dirpath);
        ImSh.Image<Rgba32> image = ImSh.Image.WrapMemory(new Memory<Rgba32>(data), width, height);
        image.Save(filepath);
        image.Dispose();
        // We unfortunately have to call this so that my synchronization method works whether or not the user decides to save their image or not
        ProcessingLibrary.TryReportCallback(1.0f, callID);
        // Technically this is also a processing step so it's fair I suppose
    }
}

public class RunnerException : Exception
{
    public RunnerException(string message) : base(message) { }
}
