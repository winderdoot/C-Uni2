using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImSh = SixLabors.ImageSharp;

namespace MinImage;
using CommandType = CommandInfo.CommandType;
public static class CommandParser
{
    private static readonly Dictionary<string, CommandType> _commandDict = new()
    {
        ["help"] = CommandType.Help,
        ["input"] = CommandType.Input,
        ["generate"] = CommandType.Generate,
        ["output"] = CommandType.Output,
        ["blur"] = CommandType.Blur,
        ["randcir"] = CommandType.RandomCircles,
        ["room"] = CommandType.Room,
        ["colorcor"] = CommandType.ColorCorrection,
        ["gamma"] = CommandType.GammaCorrection,
        ["noisy"] = CommandType.Noisy,
        ["wosy"] = CommandType.Wosy,
        ["dirsey"] = CommandType.DirectionalNoise,
        ["noicross"] = CommandType.NoisyCross,
        ["pcross"] = CommandType.PointCross,
        ["fil3"] = CommandType.Filter3,


        //["neofetch"] = CommandType.Neofetch,
        ["music"] = CommandType.Music,
        ["yolo"] = CommandType.DisableWarnings,
        ["exit"] = CommandType.Exit,
    };

    private static readonly Dictionary<CommandType, Type[]> _argTypes = new()
    {
        [CommandType.Input] = [typeof(string)], // Input allows for more arguments of type string
        [CommandType.Generate] = [typeof(int), typeof(int), typeof(int)],
        [CommandType.Output] = [typeof(string)],
        [CommandType.Blur] = [typeof(int), typeof(int)],
        [CommandType.RandomCircles] = [typeof(int), typeof(float)],
        [CommandType.Room] = [typeof(float), typeof(float), typeof(float), typeof(float)],
        [CommandType.ColorCorrection] = [typeof(float), typeof(float), typeof(float)],
        [CommandType.GammaCorrection] = [typeof(float)],
        [CommandType.Noisy] = [typeof(int), typeof(int), typeof(int), typeof(int)],
        [CommandType.Wosy] = [typeof(int), typeof(int), typeof(int), typeof(int)],
        [CommandType.DirectionalNoise] = [typeof(int), typeof(int), typeof(int), typeof(int)],
        [CommandType.NoisyCross] = [typeof(int), typeof(bool)],
        [CommandType.PointCross] = [typeof(float), typeof(float)],
        [CommandType.Filter3] = [typeof(int)],
    };

    private static int _tooBigNValue = 100;

    public static void DisableBigNWarrnings() { _tooBigNValue = Int32.MaxValue; }

    public static CommandInfo[] Parse(string command)
    {
        command.Trim().ToLower();
        string[] segments = command.Split("|");
        if (segments.Any(s => s.Length == 0))
        {
            throw new ParserException("Invalid syntax: empty command in a pipe sequence.");
        }
        List<CommandInfo> chain = new();
        for (int i = 0; i < segments.Length; i++)
        {
            string[] args = segments[i].Trim().Split(" ");
            if (!_commandDict.TryGetValue(args[0], out CommandType ctype))
            {
                throw new ParserException($"Invalid syntax: command {args[0]} not found.");
            }
            if (i == 0)
            {
                if (CommandInfo.Processors.Contains(ctype))
                {
                    throw new ParserException($"Invalid syntax: command {args[0]} is used for processing and cannot start a chain. Try piping into it ;^).");
                }
                else if (!CommandInfo.Generators.Contains(ctype) && segments.Length > 1)
                {
                    throw new ParserException($"Invalid syntax: command {args[0]} is standalone and cannot be piped into other commands.");
                }
            }
            else
            {
                if (!CommandInfo.Processors.Contains(ctype))
                {
                    throw new ParserException($"Invalid syntax: command {args[0]} cannot be piped into.");
                }
            }
            string[]? arguments = (args.Length > 1 ? args[1..] : new string[0]);

            // PARSING ARGUMENTS
            object[] boxedArguments = ParseArguments(ctype, arguments);

            chain.Add(new CommandInfo { Type = ctype, Arguments = boxedArguments });
        }
        return chain.ToArray();
    }

    public static object[] ParseArguments(CommandType ctype, string[] args)
    {
        switch (ctype)
        {
            case CommandType.NoisyCross:
                if (args.Length < 1)
                {
                    throw new ParserException($"Invalid arguments: 1 argument need to be provided.");
                }
                if (Int32.TryParse(args[0], out int N2))
                {
                    if (N2 <= 0)
                    {
                        throw new ParserException($"Invalid argument value: argument 1 is expected to be a positive integer, found {N2}.");
                    } 
                }
                goto case CommandType.RandomCircles;
            case CommandType.Exit:
                System.Environment.Exit(0);
                break;
            case CommandType.Help:
                if (args.Length == 0)
                {
                    return [];
                }
                else if (args.Length == 1)
                {
                    if (!_commandDict.ContainsKey(args[0]))
                    {
                        throw new ParserException($"Invalid arguments: {args[0]} is not a recognized command name. (see 'help')");
                    }
                    return [args[0]];
                }
                else
                {
                    throw new ParserException("Inalid arguments: The help command either takes no arguments (general help) or a single other command name. (See 'help')");
                }
            case CommandType.Music:
                goto case CommandType.DisableWarnings;
            case CommandType.DisableWarnings:
                if (args.Length != 0)
                {
                    throw new ParserException($"Invalid arguments: The {Enum.GetName(ctype.GetType(), ctype)} command expects no arguments, found {args.Length}.");
                }
                break;
            case CommandType.Input:
                if (args.Length < 1)
                {
                    throw new ParserException($"Invalid arguments: input command expects at least 1 filepath");
                }
                CheckPathValidity(args);
                return args;
            case CommandType.DirectionalNoise:
                goto case CommandType.Noisy;
            case CommandType.Wosy:
                goto case CommandType.Noisy;
            case CommandType.Noisy:
                if (args.Length < 4)
                {
                    throw new ParserException($"Invalid arguments: 4 argument need to be provided.");
                }
                if (Int32.TryParse(args[1], out int N1))
                {
                    if (N1 <= 0)
                    {
                        throw new ParserException($"Invalid argument value: argument 1 is expected to be a positive integer, found {N1}.");
                    }
                }
                goto case CommandType.RandomCircles;
            case CommandType.Generate:
                goto case CommandType.RandomCircles;
            case CommandType.RandomCircles:
                if (args.Length < 1)
                {
                    throw new ParserException($"Invalid arguments: at least 1 argument needs to be provided.");
                }
                if (Int32.TryParse(args[0], out int N))
                {
                    if (N <= 0)
                    {
                        throw new ParserException($"Invalid argument value: argument 1 is expected to be a positive integer, found {N}.");
                    }
                    if (N > _tooBigNValue)
                    {
                        throw new ParserException($"Argument warrning: argument 1 is too large, recomended value < {_tooBigNValue}. To disable this, and other warnings type 'yolo'");
                    }
                }
                goto default;

            default:
                if (args.Length != _argTypes[ctype].Length)
                {
                    throw new ParserException($"Invalid arguments: command {Enum.GetName(ctype.GetType(), ctype)} expects {_argTypes[ctype].Length} arguments, found {args.Length}.");
                }
                object[] parsedArgs = new object[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    try
                    {
                        parsedArgs[i] = Convert.ChangeType(args[i], _argTypes[ctype][i], NumberFormatInfo.InvariantInfo);
                    }
                    catch (InvalidCastException)
                    {
                        throw new ParserException($"Invalid argument type: argument {i+1}: [{args[i]}] of type {args[i].GetType().Name} could not be converted to type {_argTypes[ctype][i].Name}");
                    }
                    catch (Exception)
                    {
                        throw new ParserException($"Invalid argument type: argument {i + 1} [{args[i]}] of type {args[i].GetType().Name} either can't be recongized by expected type {_argTypes[ctype][i].Name}, or it's value is incorrect. (for floats try 0.xxx)");
                    }
                }

                // CHECKING FOR ints/floats that are outisde of expected bounds
                BoundChecking(ctype, parsedArgs);
                return parsedArgs;

        }
        return [];
    }

    private static void BoundChecking(CommandType commandType, object[] parsedArgs)
    {
        switch (commandType)
        {
            case CommandType.DirectionalNoise:
                goto case CommandType.Generate;
            case CommandType.Wosy:
                goto case CommandType.Generate;
            case CommandType.Noisy:
                goto case CommandType.Generate;
            case CommandType.Generate:
                if ((int)parsedArgs[^1] <= 0 || (int)parsedArgs[^2] <= 0)
                {
                    throw new ParserException($"Invalid argument value: {Enum.GetName(commandType.GetType(), commandType)} (argument 1,2) width/height of an image must be a positive integer.");
                }
                break;
            case CommandType.Room:
                float x1 = (float)parsedArgs[0];
                float y1 = (float)(parsedArgs[1]);
                float x2 = (float)(parsedArgs[2]);
                float y2 = (float)(parsedArgs[3]);
                if (x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1 || y1 < 0 || y1 > 1 || y2 < 0 || y2 > 1)
                {
                    throw new ParserException($"Invalid argument value: room coordinate values must belong to range [0,1].");
                }
                break;
            case CommandType.PointCross:
                x1 = (float)parsedArgs[0];
                y1 = (float)parsedArgs[1];
                if (x1 < 0 || x1 > 1 || y1 < 0 || y1 > 1)
                {
                    throw new ParserException($"Invalid argument value: pcross coordinate values must belong to range [0,1].");
                }
                break;
        }

    }

    // Checks if paths are valid images
    private static bool CheckPathValidity(string[] paths)
    {
        
        for (int i = 0; i < paths.Length; i++)
        {
            string filepath = $"{ConsoleManager.CWD}\\{paths[i]}";
            try
            {
                ImSh.Image image = ImSh.Image.Load(filepath);
                image.Dispose();
            }
            catch (NotSupportedException)
            {
                throw new ParserException($"Input error: the image format of {paths[i]} is not supported.");
            }
            catch (InvalidImageContentException)
            {
                throw new ParserException($"Input error: the image {paths[i]} contains invalid content.");
            }
            catch (UnknownImageFormatException)
            {
                throw new ParserException($"Input error: the image {paths[i]} is of unknown format.");
            }
            catch (Exception)
            {
                throw new ParserException($"Input error: The file {paths[i]} doesn't exist or couldn't be loaded.");
            }
        }
        return true;
    }
}

public class ParserException : Exception
{
    public ParserException(string message) : base(message) {}
}