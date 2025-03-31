using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
//using Microsoft.VisualStudio.Utilities; - I replaced the MS Utilities CircularBuffer<T> with my own implementation that suited the use case better

namespace MinImage;

/// <summary>
/// This class manages all console input and output in the command line app. It saves all console content to a <see cref="StringBuilder"/> object, so that it can
/// dynamically redraw progress status without loosing the content of the terminal window. It also implements command history and allows the user to search through it!
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
///       
/// </item>
/// 
/// 
/// </list>
/// 
/// </remarks>
public static class ConsoleManager
{
    private static readonly ConsoleColor defaultColor = ConsoleColor.Gray;
    private static readonly ConsoleColor promptColor = ConsoleColor.Green;
    private static readonly ConsoleColor errorCollor = ConsoleColor.Red;
    private static readonly ConsoleColor progressBarFill = ConsoleColor.DarkCyan;
    private static readonly ConsoleColor progressBarEdge = ConsoleColor.DarkGray;
    private static readonly ConsoleColor pathColor = ConsoleColor.Blue;

    private static string _userName = "C00lKittenZI37";
    private static string _prompt;

    private static readonly int _histSize = 1024;
    private static readonly CircularBuffer<string> _commandHistory = new(capacity: _histSize, defaultVal: "");
    private static StringBuilder _windowHistory = new();

    private static int _numStages;
    private static int _numHashesPerStage;
    private static int _routineCount;
    private static float[,] _progressTable = new float[0,0]; // Row - routine, column - stage in the command chain

    private static string _cwdPath; // In the sense of the simulated console app - directory where saving happens
    private static string _displayCWD;
    public static string CWD { get => _cwdPath; }

    static ConsoleManager()
    {
        string workingDirectory = Environment.CurrentDirectory;
        string projectDirectory = Directory.GetParent(workingDirectory)!.Parent!.Parent!.FullName;
        _cwdPath = projectDirectory + $"\\home_dir";
        _displayCWD = "/home_dir";
        _prompt = $"{_userName}@MinImage:{_displayCWD}$ ";
    }

    public static void SetUserName(string userName)
    {
        _userName = userName;
    }
    private static void DisplayPrompt()
    {
        string[] parts = _prompt.Split('$', 2);
        string[] userAndPath = parts[0].Split(':', 2);
        Console.ForegroundColor = promptColor;
        Console.Write(userAndPath[0]);
        Console.ForegroundColor = defaultColor;
        Console.Write(":");
        Console.ForegroundColor = pathColor;
        Console.Write(userAndPath[1]);
        Console.ForegroundColor = defaultColor;
        Console.Write("$ ");
    }

    public static void LogError(string error)
    {
        _windowHistory.Append($">{error}\n");
    }

    /// <summary>
    /// Reads input characters untill <b>ENTER</b> is pressed, then returns the read line.
    /// It also allows for special commands such as arrows for history searching, <b>CTRL</b>+<b>L</b> for clearing the command window.
    /// </summary>
    /// <returns><see cref="string"/> object containing the read line</returns>
    public static string ReadLine()
    {
        _commandHistory.ResetSearchIndex();
        ConsoleKeyInfo key;
        Console.TreatControlCAsInput = true;
        StringBuilder line = new StringBuilder();
        int position = 0;

        while (true)
        {
            Console.CursorVisible = false;
            Console.SetCursorPosition(0, Console.CursorTop);
            DisplayWindowHistory();
            DisplayPrompt();
            Console.Write(line.ToString());
            Console.SetCursorPosition(position + _prompt.Length, Console.CursorTop);
            Console.CursorVisible = true;

            key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (position > 0)
                    {
                        position--;
                        continue;
                    }
                    break;
                case ConsoleKey.RightArrow:
                    if (position < line.Length)
                    {
                        position++;
                        continue;
                    }
                    break;
                case ConsoleKey.UpArrow:
                    if (line.Length > 0)
                        _commandHistory.ReplaceSearched(line.ToString());
                    line = new StringBuilder(_commandHistory.GetPrevious());
                    position = line.Length;
                    continue;
                case ConsoleKey.DownArrow:
                    if (line.Length > 0)
                        _commandHistory.ReplaceSearched(line.ToString());
                    line = new StringBuilder(_commandHistory.GetNext());
                    position = line.Length;
                    continue;
                case ConsoleKey.Enter:
                    _windowHistory.Append(_prompt);
                    _windowHistory.Append(line.ToString());
                    _windowHistory.Append("\n");
                    if (line.Length > 0)
                    {
                        _commandHistory.Add(line.ToString());
                        _commandHistory.ResetSearchIndex();
                        return line.ToString();
                    }
                    continue;
                case ConsoleKey.Backspace:
                    if (position > 0)
                    {
                        line.Remove(--position, 1);
                        continue;
                    }
                    break;
            }
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 )
            {
                if (key.Key == ConsoleKey.L)
                {
                    _windowHistory.Clear();
                    continue;
                }
                continue;
            }
            if (KeyIsArrow(key.Key) || key.Key == ConsoleKey.Backspace)
            {
                continue;
            }
            line.Insert(position, key.KeyChar);
            position++;
        }
    }
    
    private static bool KeyIsArrow(ConsoleKey key)
    {
        return key == ConsoleKey.DownArrow || key == ConsoleKey.UpArrow || key == ConsoleKey.LeftArrow || key == ConsoleKey.RightArrow;
    }
    /// <summary>
    /// Redraws the window history and does custom coloring of lines based on these rules:
    /// <list type="number">
    /// <item>Color the line with <see cref="promptColor"/> if it doesn't begin with  <![CDATA['>' or '[']]> untill '$' is reached. Afterwards switch to <see cref="defaultColor"/>.</item>
    /// <item>Color the line with <see cref="errorCollor"/> if it starts with  <![CDATA['>']]></item>
    /// <item>Special coloring is defined for lines starting with ':'</item>
    /// </list>`
    /// </summary>
    /// 
    private static int NumLines()
    {
        int c = 0;
        for (int i = 0; i < _windowHistory.Length; i++)
        {
            if (_windowHistory[i] == '\n')
            {
                c++;
            }
        }
        return c;
    }
    private static void DisplayWindowHistory()
    {
        Console.Clear(); // This doesn't clear the entire thing :(((((((((
        string[] lines = _windowHistory.ToString().Split("\n");
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length < 1)
            {
                continue;
            }
            switch (lines[i][0])
            {
                case ' ':
                    Console.WriteLine();
                    break;
                case '>':
                    Console.ForegroundColor = errorCollor;
                    Console.Write(lines[i]);
                    Console.ForegroundColor = defaultColor;
                    break;
                case ':':
                    Console.ForegroundColor = defaultColor;
                    Console.Write(lines[i]);
                    Console.ForegroundColor = defaultColor;
                    break;
                default:
                    string[] parts = lines[i].Split('$', 2);
                    string[] userAndPath = parts[0].Split(':', 2);
                    Console.ForegroundColor = promptColor;
                    Console.Write(userAndPath[0]);
                    Console.ForegroundColor = defaultColor;
                    Console.Write(":");
                    Console.ForegroundColor = pathColor;
                    Console.Write(userAndPath[1]);
                    Console.ForegroundColor = defaultColor;
                    Console.Write("$");
                    if (parts.Length > 1)
                        Console.Write(parts[1]);
                    break;
            }
            Console.WriteLine();
        }
    }

    private static readonly string boat = """
   .  o ..
 o . o o.o
      ...oo
        __[]__
     __|_o_o_o\__
     \''''''''' /
      \. ..  . /
 ^^^^^^^^^^^^^^^^^^^^
 """;

    public static readonly string[] boatLines = boat.Split("\n");

    public static async Task DisplayBoatAnimation(CancellationToken Token)
    {
        Console.CursorVisible = false;
        StringBuilder offset = new();
        while (!Token.IsCancellationRequested)
        {
            DisplayWindowHistory();
            Console.WriteLine();
            if (offset.Length > Console.WindowWidth - 25)
                offset.Clear();
            
            foreach (var line in boatLines)
            {
                Console.Write(offset.ToString());
                Console.WriteLine(line);
            }
            await Task.Delay(150);
            offset.Append(" ");
        }
        Console.CursorVisible = true;
        return;
    }

    public static void InitProgressReport(int routineCount, int chainLength)
    {
        _numStages = chainLength;
        _routineCount = routineCount;
        _progressTable = new float[routineCount, _numStages];
        _numHashesPerStage = (int)Math.Ceiling(100.0f / _numStages);
    }

    public static void UpdateProgressReport(ProcessingLibrary library, int routineCount) // Object reference passed by value so it's ok
    {
        for (int i = 0; i < routineCount; i++)
        {
            library.GetProgressTable(_progressTable, i);
        }
    }

    private static int calculateDisplayPercetage(int callID)
    {
        float sum = 0.0f;
        for (int stage = 0; stage < _numStages; stage++)
        {
            sum += _progressTable[callID, stage];
        }
        return (int)Math.Ceiling(100.0f*sum / _numStages);
    }

    public static void DisplayProgressReport()
    {
        Console.CursorVisible = false;
        DisplayWindowHistory();
        for (int i = 0; i < _routineCount; i++)
        {
            Console.ForegroundColor = progressBarEdge;
            Console.Write("[");
            for (int stage = 0; stage < _numStages; stage++)
            {
                int completedHashes = (int)Math.Floor(_progressTable[i, stage] * _numHashesPerStage);
                Console.ForegroundColor = progressBarFill;
                Console.Write(new string('#', completedHashes));
                Console.ForegroundColor = progressBarEdge;
                Console.Write(new string('-', _numHashesPerStage - completedHashes));
                if (stage < _numStages - 1)
                {
                    Console.Write('|');
                }
            }
            Console.Write("]");
            Console.ForegroundColor = progressBarFill;
            Console.WriteLine($" {calculateDisplayPercetage(i)}%");
        }
        Console.ForegroundColor = defaultColor;
    }

    public static void HelpCommand(object[] args)
    {
        var assembly = Assembly.GetExecutingAssembly()!;
        
        if (args.Length == 0)
        {

            var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("help_general.txt"));
            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using StreamReader reader = new StreamReader(stream);
            _windowHistory.Append('\n');
            _windowHistory.Append(reader.ReadToEnd());
        }
        else
        {
            var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("help_unimplemented.txt"));
            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using StreamReader reader = new StreamReader(stream);
            _windowHistory.Append('\n');
            _windowHistory.Append(reader.ReadToEnd());
        }
    }
}
