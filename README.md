# DictExt

**⚠️This is a vibe-coding project!**

Only use the project with extreme caution.
This project heavily depends on Vibe coding.
Unexpected behavior may occur.


DictExt is a PowerToys Command Palette extension for looking up words in local MDict `.mdx` dictionaries.

The extension is self-contained: the MDict parser source is included under `DictExt/MdictSharp`, and no local dictionary files are committed or embedded.

## Features

- Search `.mdx` dictionary headwords from Command Palette.
- Open a result to view the dictionary article.
- Use global fallback queries with the `lookup ` prefix, for example `lookup hello`.
- Configure one `.mdx` file path or folder path per line.
- Automatically scan the default dictionary folder. When unpackaged this is:
  `%LOCALAPPDATA%\Microsoft.CmdPal\Dictionaries`

  When packaged, Windows redirects it to the app package `LocalState\Dictionaries` folder.

## Dictionary Setup

Open the extension settings and add dictionary paths under `Dictionary paths`.

Each line can be either:

```text
C:\Users\you\Documents\dictionaries
C:\Users\you\Documents\dictionaries\example.mdx
```

Folder paths are scanned for `*.mdx` files in that folder only. Subfolders are not scanned.

Alternatively, copy `.mdx` files into the default folder shown above.

## Usage

In Command Palette:

1. Open `Dictionary`.
2. Type a word.
3. Select a matching headword to view the article.

From the global Command Palette search box:

```text
lookup hello
```

The fallback only activates for queries that start with `lookup `.

## Supported Dictionary Format

Current parser support is intentionally narrow:

- MDict 2.0
- UTF-8 and UTF-16 dictionary text
- zlib and uncompressed blocks
- encrypted headword index
- stylesheet substitution

Not currently supported:

- `.mdd` resource files
- LZO compression
- GBK / GB18030 dictionaries
- full-text search
- inflection or morphology matching

## Build

Requirements:

- Windows
- .NET SDK compatible with `net9.0-windows10.0.26100.0`

Build:

```powershell
dotnet build DictExt.sln
```

## Notes

Article rendering uses Command Palette `MarkdownContent`, so HTML and CSS support depends on the host renderer. The extension injects inline font styles for Chinese dictionary text, but renderer-level sanitization may still limit font control.
