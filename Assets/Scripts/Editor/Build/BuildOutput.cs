using System;
using System.Globalization;
using System.IO;

namespace Client.Editor.Build
{
    public static class BuildOutput
    {
        // This reserves a run directory only. A real pipeline must set the final status and write ResultFile.
        public static BuildResult Create(BuildRequest request)
        {
            BuildPreflight.ValidateRequest(request);
            string root = Path.GetFullPath(request.OutputRoot);
            SourceFileTransaction.RejectReparsePoints(root);
            Directory.CreateDirectory(root);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                string name = "Build_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                string output = Path.Combine(root, name);
                if (File.Exists(output) || Directory.Exists(output)) continue;
                SourceFileTransaction.RejectReparsePoints(output);
                Directory.CreateDirectory(output);
                // The exclusive marker also guards a collision between independent owners of an output root.
                try { using (new FileStream(Path.Combine(output, ".build-owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { } }
                catch (IOException) { continue; }
                return new BuildResult(output);
            }
            throw new IOException("Cannot reserve a fresh build output directory.");
        }
    }
}
