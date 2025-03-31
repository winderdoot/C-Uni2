using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinImage;
public struct CommandInfo
{
    public enum CommandType
    { 
        Help,
        Input,
        Generate,
        Output,
        Blur,
        RandomCircles,
        Room,
        ColorCorrection,
        GammaCorrection,

        Noisy,
        Wosy,
        DirectionalNoise,
        NoisyCross,
        PointCross,
        Filter3,

        Neofetch, // Stupid ideas
        Music,
        DisableWarnings,
        Exit,
    }

    public CommandType Type;
    public object[] Arguments;

    public bool IsGenerator() { return Generators.Contains(Type); }
    public bool IsProcessing() { return Processors.Contains(Type); }

    public CommandInfo(CommandType type, string[] arguments)
    {
        Type = type;
        Arguments = arguments;
        //string? xx = Enum.GetName(type.GetType(), type);
    }

    public static HashSet<CommandType> Generators = new() 
        { 
            CommandType.Generate,
            CommandType.Input,
            CommandType.Noisy,
            CommandType.Wosy,
            CommandType.DirectionalNoise,
        };

    public static HashSet<CommandType> Processors = new()
        {
            CommandType.Output,
            CommandType.Blur,
            CommandType.RandomCircles,
            CommandType.Room,
            CommandType.ColorCorrection,
            CommandType.GammaCorrection,
            CommandType.NoisyCross,
            CommandType.PointCross,
            CommandType.Filter3,
        };
}



