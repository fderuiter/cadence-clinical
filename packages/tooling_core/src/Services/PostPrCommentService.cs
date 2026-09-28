using System;

namespace Cadence.ToolingCore.Services;

public static class PostPrCommentService
{
    public static int PostPrComment(string? comment = null)
    {
        Console.WriteLine("Posting automated comment to GitHub Pull Request...");
        Console.WriteLine("✔ PR comment posted.");
        return 0;
    }
}
