using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.AI.Configuration
{
    public static class ModelSettings
    {
        // Direct link to the Qwen 3 VL 8B Instruct model in GGUF format
        public const string DownloadUrl = "https://modelscope.ai/models/Qwen/Qwen3-VL-8B-Instruct-GGUF/resolve/master/Qwen3VL-8B-Instruct-Q4_K_M.gguf";

        public const string FileName = "Qwen3VL-8B-Instruct-Q4_K_M.gguf";
        public const long ExpectedSizeBytes = 5030000000L;
    }
}
