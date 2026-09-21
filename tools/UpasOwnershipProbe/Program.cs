using System;
using NINA.Plugins.PolarAlignment;

// Test-only helper. No NINA, device or serial references; caller supplies a TEMP lock path.
if (args.Length != 2 || (args[1] != "try" && args[1] != "hold")) return 2;
try {
    using var ownership = new UpasSerialOwnership(args[0]);
    Console.WriteLine("LOCKED");
    if (args[1] == "hold") Console.ReadLine();
    return 0;
} catch (System.IO.IOException e) {
    Console.Error.WriteLine(e.Message);
    return 3;
}
