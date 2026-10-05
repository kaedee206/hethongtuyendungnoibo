using System;
using System.IO;

class Program {
    static void Main(string[] args) {
        var lines = File.ReadAllLines(args[0]);
        int balance = 0;
        int lastOpenLine = -1;
        for (int i = 0; i < lines.Length; i++) {
            foreach (char c in lines[i]) {
                if (c == '(') {
                    balance++;
                    lastOpenLine = i + 1;
                } else if (c == ')') {
                    balance--;
                }
            }
        }
        Console.WriteLine($"Final balance: {balance}, Last Open Line: {lastOpenLine}");
    }
}
