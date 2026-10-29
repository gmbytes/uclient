[CmdletBinding()]param([Parameter(Mandatory)][string]$RunRoot,[Parameter(Mandatory)][string]$ReleaseRoot,[Parameter(Mandatory)][int]$Port)
$ErrorActionPreference='Stop'
$listener=[Net.HttpListener]::new();$listener.Prefixes.Add("http://127.0.0.1:$Port/");$listener.Start()
[IO.File]::WriteAllText((Join-Path $RunRoot 'server-ready'),[string]$PID)
try {
    $pending=$listener.GetContextAsync()
    while(-not(Test-Path -LiteralPath (Join-Path $RunRoot 'server-stop'))) {
        if(-not$pending.Wait(100)){continue}
        $context=$pending.Result;$started=[DateTime]::UtcNow;$bytes=0;$status=404;$path=$context.Request.Url.AbsolutePath
        try {
            $active=[IO.File]::ReadAllText((Join-Path $RunRoot 'active-release.txt')).Trim()
            $base=[IO.Path]::GetFullPath((Join-Path $ReleaseRoot $active))
            $relative=[Uri]::UnescapeDataString($path).TrimStart('/')
            $file=[IO.Path]::GetFullPath((Join-Path $base $relative))
            if($context.Request.HttpMethod-ne'GET' -or -not$file.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid path.'}
            if(Test-Path -LiteralPath $file -PathType Leaf){$data=[IO.File]::ReadAllBytes($file);$context.Response.ContentLength64=$data.Length;$context.Response.OutputStream.Write($data,0,$data.Length);$bytes=$data.Length;$status=200}else{$context.Response.StatusCode=404}
        }catch{$status=500;$context.Response.StatusCode=500}
        finally{$context.Response.Close();[IO.File]::AppendAllText((Join-Path $RunRoot 'server-requests.jsonl'),((@{utc=$started;path=$path;status=$status;bytes=$bytes;active=$active}|ConvertTo-Json -Compress)+"`n"))}
        $pending=$listener.GetContextAsync()
    }
}finally{$listener.Stop();$listener.Close()}
