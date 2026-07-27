using System;
using System.IO;
using KROT.Infrastructure.Storage;

namespace KROT.FieldTest;

public static class FieldTestPaths
{
    public static string ReportsDirectory =>
        Path.Combine(AppPaths.UserDataRoot, "field-tests");

    public static string CreateReportPath() =>
        Path.Combine(ReportsDirectory, $"KROT-field-test-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
}

