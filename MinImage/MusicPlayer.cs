using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinImage;

public static class MusicPlayer
{
    private static readonly int beat = 310;//1109 1175
    public static readonly (int, int)[] magicFrequencies =
        [
            (740, 7), (659, 1), (740, 1), (784, 1), (740, 1), (659, 1), (587, 6), (0, 3), (659, 2), (740, 1), (784, 3),
            (784, 5), (784, 1), (784, 1), (784, 1), (740, 1), (659, 3), (659, 5), (440, 1), (587, 2), (659, 1), (740, 3),
            (740, 5), (659, 1), (740, 2), (659, 1), (587, 3), (587, 4), (0, 2), (587, 3), (988, 3), (988, 4), (988, 1),
            (1109, 1), (1175, 1), (1109, 1), (988, 1), (880, 3), (880, 4), (0, 2), (784, 2), (740, 1), (784, 3), (784, 4),
            (784, 1), (880, 1), (988, 1), (880, 1), (784, 1), (740, 3), (740, 4), (0, 2), (587, 2), (587, 1), (988, 3),
            (988, 4), (988, 1), (1109, 1), (1175, 1), (1109, 1), (988, 1), (880, 3), (880, 4), (0, 2), (784, 2), (740, 1),
            (784, 3), (784, 4), (659, 1), (740, 1), (784, 1), (740, 1), (659, 1), (587, 7)
        ];
    public static async Task PlayMusic()
    {
        CancellationTokenSource tokenSource = new();
        Task animation = Task.Run(() => ConsoleManager.DisplayBoatAnimation(tokenSource.Token));
        foreach ((int frec, int time) in magicFrequencies)
        {
            if (frec == 0)
                await Task.Delay(time * beat);
            else
                Console.Beep(frec, time * beat);
        }
        tokenSource.Cancel();
        await animation;
    }
}
