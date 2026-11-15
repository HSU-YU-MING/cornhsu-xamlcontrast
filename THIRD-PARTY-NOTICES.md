# Third-party notices

XamlContrast ships data derived from the following projects. Their licences are reproduced below.

## microsoft/microsoft-ui-xaml (WinUI)

`src/XamlContrast.Core/WinUiTheme.json` lists the default colours of WinUI's Fluent theme resources
(`TextFillColorPrimaryBrush`, `SystemControlForegroundAltHighBrush`, …), read from
`controls/dev/CommonStyles/Common_themeresources_any.xaml` and `dxaml/xcp/dxaml/themes/generic.xaml`
at the commit named in the file's `source` field. Source: https://github.com/microsoft/microsoft-ui-xaml

```
MIT License

Copyright (c) Microsoft Corporation. All rights reserved.

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
SOFTWARE
```

## lepoco/wpfui (WPF-UI)

`src/XamlContrast.Core/WpfUiTheme.json` lists the default colours of WPF-UI's built-in theme
(`ApplicationBackgroundBrush`, `TextFillColorPrimaryBrush`, …), read from
`src/Wpf.Ui/Resources/Theme/Dark.xaml` and `Light.xaml` at the commit named in the file's `source`
field. High-contrast themes (`HC*.xaml`) and keys derived from the user's accent colour are not
included. Source: https://github.com/lepoco/wpfui

The source files carry this header:

```
This Source Code Form is subject to the terms of the MIT License.
If a copy of the MIT was not distributed with this file, You can obtain one at
https://opensource.org/licenses/MIT.
Copyright (C) Leszek Pomianowski and WPF UI Contributors.
All Rights Reserved.

Based on Microsoft XAML for Win UI
Copyright (c) Microsoft Corporation. All Rights Reserved.
```

and the MIT License reads:

```
MIT License

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
