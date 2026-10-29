#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseRoot,
    [Parameter(Mandatory)][string]$ControlRoot,
    [Parameter(Mandatory)][string]$ListenAddress,
    [Parameter(Mandatory)][ValidateRange(1024,65535)][int]$Port,
    [ValidateRange(0,200)][int]$ChunkDelayMilliseconds=0
)
$ErrorActionPreference='Stop'
$address=[Net.IPAddress]::Parse($ListenAddress)
if(-not[Net.IPAddress]::IsLoopback($address)){throw 'This test server requires an explicit loopback address; use an owned adb reverse for Android.'}
$ReleaseRoot=[IO.Path]::GetFullPath($ReleaseRoot);$ControlRoot=[IO.Path]::GetFullPath($ControlRoot)
if(-not(Test-Path -LiteralPath $ControlRoot -PathType Container)){throw 'Create an owned control directory before starting this server.'}
function NoReparse([string]$Path){for($entry=$Path;$entry;$entry=[IO.Path]::GetDirectoryName($entry)){if((Test-Path -LiteralPath $entry)-and((Get-Item -LiteralPath $entry -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'Release server cannot traverse reparse points.'}}}
NoReparse $ReleaseRoot;NoReparse $ControlRoot
$listener=[Net.HttpListener]::new();$listener.Prefixes.Add("http://$($address.ToString()):$Port/");$listener.Start()
[IO.File]::WriteAllText((Join-Path $ControlRoot 'server-ready'),[string]$PID)
try {
    $pending=$listener.GetContextAsync()
    while(-not(Test-Path -LiteralPath (Join-Path $ControlRoot 'server-stop'))){
        if(-not$pending.Wait(100)){continue};$context=$pending.Result;$status=404;$bytes=0;$active=$null;$path=$context.Request.Url.AbsolutePath;$started=[DateTime]::UtcNow
        try {
            $active=[IO.File]::ReadAllText((Join-Path $ControlRoot 'active-release.txt')).Trim()
            if([IO.Path]::IsPathRooted($active)-or($active-split'[/\\]')-contains'..'){throw 'Invalid release selection.'}
            $base=[IO.Path]::GetFullPath((Join-Path $ReleaseRoot $active))
            if(-not$base.StartsWith($ReleaseRoot.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Release selection escapes its owned root.'}
            $relative=[Uri]::UnescapeDataString($path).TrimStart('/')
            if($relative.Contains('\')-or$relative.Split('/')-contains'..'-or$relative.Split('/')-contains'.'-or$relative.Contains(':')){throw 'Invalid release URL path.'}
            $file=[IO.Path]::GetFullPath((Join-Path $base $relative));NoReparse $file
            if($context.Request.HttpMethod-ne'GET'-or-not$file.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid request.'}
            if(Test-Path -LiteralPath $file -PathType Leaf){
                $input=[IO.File]::OpenRead($file)
                try{$context.Response.ContentLength64=$input.Length;$buffer=[byte[]]::new(16384);while(($count=$input.Read($buffer,0,$buffer.Length))-gt0){$context.Response.OutputStream.Write($buffer,0,$count);$context.Response.OutputStream.Flush();$bytes+=$count;if($ChunkDelayMilliseconds){Start-Sleep -Milliseconds $ChunkDelayMilliseconds}};$status=200}
                finally{$input.Dispose()}
            }else{$context.Response.StatusCode=404}
        }catch{$status=500;try{$context.Response.StatusCode=500}catch{}}
        finally{$context.Response.Close();$line=@{utc=$started;finishedUtc=[DateTime]::UtcNow;path=$path;status=$status;bytes=$bytes;active=$active;serverPid=$PID}|ConvertTo-Json -Compress;[IO.File]::AppendAllText((Join-Path $ControlRoot 'server-requests.jsonl'),$line+"`n",[Text.UTF8Encoding]::new($false))}
        $pending=$listener.GetContextAsync()
    }
}finally{$listener.Stop();$listener.Close()}
