$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase,PresentationFramework
$taskSvg=[xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot 'assets\mark.svg'))
$taskGeometry=[Windows.Media.Geometry]::Parse($taskSvg.svg.path.d)
$taskAccent=[Windows.Media.BrushConverter]::new().ConvertFromString('#00E8D2')
$taskBackground=[Windows.Media.BrushConverter]::new().ConvertFromString('#0C1012')
$taskImages=@()
foreach($taskSize in @(16,20,24,32,48,64,256)){
 $taskVisual=[Windows.Media.DrawingVisual]::new();$taskContext=$taskVisual.RenderOpen()
 $taskContext.PushTransform([Windows.Media.ScaleTransform]::new($taskSize/128.0,$taskSize/128.0))
 $taskContext.DrawRoundedRectangle($taskBackground,$null,[Windows.Rect]::new(0,0,128,128),28,28)
 $taskContext.DrawGeometry($taskAccent,$null,$taskGeometry);$taskContext.Pop();$taskContext.Close()
 $taskBitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($taskSize,$taskSize,96,96,[Windows.Media.PixelFormats]::Pbgra32);$taskBitmap.Render($taskVisual)
 $taskEncoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$taskEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
 $taskMemory=[IO.MemoryStream]::new();$taskEncoder.Save($taskMemory);$taskImages+=,@{Size=$taskSize;Data=$taskMemory.ToArray()};$taskMemory.Dispose()
 if($taskSize -eq 256){[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'assets\mark.png'),$taskImages[-1].Data)}
}
$taskOutput=[IO.File]::Create((Join-Path $PSScriptRoot 'assets\BlockMook.ico'));$taskWriter=[IO.BinaryWriter]::new($taskOutput)
try{
 $taskWriter.Write([uint16]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]$taskImages.Count);$taskOffset=6+16*$taskImages.Count
 foreach($taskImage in $taskImages){$taskDimension=if($taskImage.Size -eq 256){0}else{$taskImage.Size};$taskWriter.Write([byte]$taskDimension);$taskWriter.Write([byte]$taskDimension);$taskWriter.Write([byte]0);$taskWriter.Write([byte]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]32);$taskWriter.Write([uint32]$taskImage.Data.Length);$taskWriter.Write([uint32]$taskOffset);$taskOffset+=$taskImage.Data.Length}
 foreach($taskImage in $taskImages){$taskWriter.Write([byte[]]$taskImage.Data)}
}finally{$taskWriter.Dispose()}
'Brand icon rendered at 16, 20, 24, 32, 48, 64 and 256 px.'
