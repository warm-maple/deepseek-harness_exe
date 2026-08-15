using System.Buffers.Binary;
using Xunit;

namespace DeepSeekHarness.Web.Tests;

public sealed class RuntimeStartupTests
{
    [Theory]
    [InlineData("dsh web: http://127.0.0.1:49152", "http://127.0.0.1:49152/")]
    [InlineData("dsh web: http://127.0.0.1:49153 (LAN: http://192.168.1.2:49153)", "http://127.0.0.1:49153/")]
    public void ParsesLoopbackServerAnnouncements(string line, string expected)
    {
        Assert.True(MainWindow.TryParseAnnouncedUri(line, out var uri));
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("dsh web: http://127.0.0.1:0")]
    [InlineData("dsh web: http://127.0.0.1")]
    [InlineData("dsh web: http://127.0.0.1:49152/other")]
    [InlineData("dsh web: http://user@127.0.0.1:49152")]
    [InlineData("dsh web: http://127.0.0.1:65536")]
    [InlineData("dsh web: https://127.0.0.1:49152")]
    [InlineData("dsh web: http://localhost:49152")]
    [InlineData("dsh web: http://192.168.1.2:49152")]
    [InlineData("runtime: http://127.0.0.1:49152")]
    [InlineData("dsh web: not-a-url")]
    public void RejectsUntrustedServerAnnouncements(string line)
    {
        Assert.False(MainWindow.TryParseAnnouncedUri(line, out _));
    }

    [Fact]
    public void AppIconContainsValidWindowsSizes()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "app.ico"));
        Assert.True(bytes.Length >= 6);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        var directoryLength = 6 + count * 16;
        Assert.True(bytes.Length >= directoryLength);

        var sizes = new HashSet<int>();
        var pngSignature = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        for (var index = 0; index < count; index++)
        {
            var entry = 6 + index * 16;
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            Assert.Equal(width, height);
            Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 4, 2)));
            Assert.Equal((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 6, 2)));

            var imageLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8, 4)));
            var imageOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 12, 4)));
            Assert.InRange(imageOffset, directoryLength, bytes.Length - 1);
            Assert.InRange(imageLength, 1, bytes.Length - imageOffset);
            var image = bytes.AsSpan(imageOffset, imageLength);
            var isPng = image.StartsWith(pngSignature);
            var dibHeaderSize = image.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(image[..4]) : 0;
            Assert.True(isPng || dibHeaderSize is 40 or 108 or 124, $"{width}px entry has no PNG or supported DIB header");
            sizes.Add(width);
        }

        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, sizes.Order());
    }

    [Fact]
    public void InstallerExcludesBuildMachineWebView2Data()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DeepSeekHarnessWeb.iss"));
        Assert.Contains(@"DeepSeekHarnessWeb.exe.WebView2\*", script, StringComparison.Ordinal);
    }
}
