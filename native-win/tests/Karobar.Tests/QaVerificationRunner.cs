using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Karobar.Tests;

public static class QaVerificationRunner
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==========================================================================");
        Console.WriteLine(" Karobar-App ERP v2 Native Windows Migration — 12-Phase QA Suite Runner");
        Console.WriteLine("==========================================================================");

        var testClasses = typeof(QaVerificationRunner).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Any(m => m.GetCustomAttribute<FactAttribute>() != null))
            .OrderBy(t => t.Name)
            .ToList();

        var passed = 0;
        var failed = 0;
        var failures = new List<string>();
        var totalSw = Stopwatch.StartNew();

        foreach (var testClass in testClasses)
        {
            Console.WriteLine($"\n[{testClass.Name}]");
            var instance = Activator.CreateInstance(testClass);
            var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
                .ToList();

            foreach (var method in methods)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var result = method.Invoke(instance, null);
                    if (result is Task task)
                    {
                        await task;
                    }
                    sw.Stop();
                    passed++;
                    Console.WriteLine($"  PASS  {method.Name} ({sw.ElapsedMilliseconds} ms)");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    failed++;
                    var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    var detail = inner.InnerException != null
                        ? $"{inner.GetType().Name}: {inner.Message} --> {inner.InnerException.GetType().Name}: {inner.InnerException.Message}"
                        : $"{inner.GetType().Name}: {inner.Message}";
                    Console.WriteLine($"  FAIL  {method.Name} ({sw.ElapsedMilliseconds} ms)");
                    Console.WriteLine($"        {detail}");
                    failures.Add($"[{testClass.Name}.{method.Name}] {detail}");
                }
            }
        }

        totalSw.Stop();
        Console.WriteLine("\n==========================================================================");
        if (failures.Count > 0)
        {
            Console.WriteLine(" Failed Tests Summary:");
            foreach (var f in failures)
            {
                Console.WriteLine($"  - {f}");
            }
            Console.WriteLine("--------------------------------------------------------------------------");
        }
        Console.WriteLine($" QA Results: {passed} Passed, {failed} Failed (Total Time: {totalSw.ElapsedMilliseconds} ms)");
        Console.WriteLine("==========================================================================");

        return failed == 0 ? 0 : 1;
    }
}
