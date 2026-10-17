# Foam detail textures

Greyscale detail textures sampled by `ocean.gdshader` (red channel). Selected by `OceanConfig.FoamTexture`.
Any replacement must match Crest's brightness distribution (mean 0.41, p10 0.06, p90 0.73): the lace erosion thresholds assume it.

## foam_crest.png (default, `"crest"`)
- Source: https://raw.githubusercontent.com/wave-harmonic/crest/master/crest/Assets/Crest/Crest/Textures/foam.png
- Author: Wave Harmonic and contributors (Crest ocean system)
- Licence: MIT (https://raw.githubusercontent.com/wave-harmonic/crest/master/LICENSE), full notice below
- Downloaded: 2026-10-17
- Processing: none (512x512, mean 0.411, p10 0.055, p90 0.725)

```
MIT License

Copyright (c) 2019 Wave Harmonic and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## foam_ambientcg_001.png (alternative, `"ambientcg_001"`)
- Source: https://ambientcg.com/get?file=Foam001_2K-JPG.zip, map `Foam001_2K-JPG_Opacity.jpg` (ambientCG "Foam001")
- Author: Lennart Demes (ambientCG)
- Licence: CC0 1.0
- Downloaded: 2026-10-17
- Processing (`dev/AssetPrep.cs`): red channel; levels stretched from the 1st..99th percentile (0.000..0.604) to 0..1;
  gamma 0.739 so the mean is 0.41; saved as 8-bit greyscale PNG, 2048x2048 (mean 0.410, p10 0.067, p90 0.757).

## procedural (`"procedural"`)
Generated at runtime by `FoamDetailTexture.Generate()`; also the fallback when a file is missing.
