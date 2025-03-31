using System;

public static async Task RunCommand()
{
    _routinesCount = 10;
    int[] progress = new int[_routinesCount];
    object[] locks = new object[_routinesCount];
    for (int i = 0; i < _routinesCount; i++)
        locks[i] = new object();
    Task[] routines = new Task[_routinesCount];
    for (int i = 0; i < _routinesCount; i++)
    {
        int callID = i;
        routines[i] = Task.Run(async () =>
        {
            for (int j = 0; j < 10; j++)
            {
                await Task.Delay(1000);
                lock (locks[callID])
                    progress[callID]++;
            }
        });
    }
    for (int j = 0; j < 10; j++)
    {
        for (int i = 0; i < _routinesCount; i++)
        {
            lock (locks[i])
                Console.WriteLine($"{i} => {progress[i]}");
        }
        await Task.Delay(500);
    }

    await Task.WhenAll(routines);
    for (int i = 0; i < _routinesCount; i++)
    {
        lock (locks[i])
            Console.WriteLine($"{i} => {progress[i]}");
    }
}
