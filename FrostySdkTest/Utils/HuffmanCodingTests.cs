using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;
using static Frosty.Sdk.Utils.EncodingResult;

namespace FrostySdkTest.Utils;

public class HuffmanEncodingTests
{

    /// <summary>
    /// Tests the encoding and decoding of some test strings. The argument source once encodes the strings with reusing existing entries, and once without, leading to different result byte lengths.
    /// </summary>
    /// <param name="compressResults"></param>
    /// <param name="encodedBitSize"></param>
    /// <param name="endian"
    [TestCase(false, 195, 25, Endian.Little)]
    [TestCase(false, 195, 28, Endian.Big)]
    [TestCase(true, 175, 22, Endian.Little)]
    [TestCase(true, 175, 24, Endian.Big)]
    public void TestEncodingDecoding(bool compressResults, int encodedBitSize, int encodedByteSize, Endian endian)
    {
        string[] texts = { "These are ", "", "some ", "Test Texts", " for tests ", "some ", " these are" };

        HuffmanEncoder encoder = new();

        var encodingTree = encoder.BuildHuffmanEncodingTree(texts);

        int i = 0;
        var input = texts.Select(x => new Tuple<int, string>(i++, x)).ToList();

        // don't use padding here
        var encodingResult = encoder.EncodeTexts(input, endian, compressResults, false);
        Assert.Multiple(() =>
        {
            Assert.That(encodingResult.EncodedTestsAsBools, Has.Count.EqualTo(encodedBitSize), "Encoded bit count does not match expected count");
            Assert.That(encodingResult.EncodedTexts, Has.Length.EqualTo(encodedByteSize), "Encoded data-length does not match expected length");
            Assert.That(encodingResult.EncodedTextPositions, Has.Count.EqualTo(texts.Length), "Encoded text position has different number of entries than the number of encoded texts!");
        });

        var byteArray = encodingResult.EncodedTexts;

        HuffmanDecoder decoder = CreateDecoderFromTree(encodingTree);
        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {
                ds.Write(byteArray);
                ds.Position = 0;

                decoder.ReadOddSizedEncodedData(ds, (uint)byteArray.Length, endian);
            }
        }

        List<string> decoded = new();
        foreach (var textId in encodingResult.EncodedTextPositions)
        {
            string decodedText = decoder.ReadHuffmanEncodedString(textId.Position);

            decoded.Add(decodedText);
        }

        // assert that the texts can be decoded again
        Assert.Multiple(() =>
        {
            Assert.That(decoded, Has.Count.EqualTo(texts.Length), "Dedoded number of texts does not match input number of texts!");
            Assert.That(decoded.ToArray(), Is.EqualTo(texts), "Dedoded texts do not match given texts to encode!");
        });
    }

    private static HuffmanDecoder CreateDecoderFromTree(IList<uint> encodingTree)
    {
        HuffmanDecoder decoder = new();
        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {
                foreach (var val in encodingTree)
                {
                    ds.WriteUInt32(val);
                }

                ds.Position = 0;

                decoder.ReadHuffmanTable(new DataStream(stream), (uint)encodingTree.Count);
            }
        }
        return decoder;
    }

    [Test]
    public void TestWithTextAsKey()
    {
        string[] texts = { "Some ", "more ", "Text that might be ", "stored together ", "or whatever ", "these are only ", "for test usage" };

        HuffmanEncoder encoder = new();
        var encodingTree = encoder.BuildHuffmanEncodingTree(texts);

        HuffmanEncodedTextArray<string> encodingResult = encoder.EncodeTexts(texts.Select(
            x => new Tuple<string, string>(x, x)).ToList(), Endian.Little, false);

        var byteArray = encodingResult.EncodedTexts;

        HuffmanDecoder decoder = CreateDecoderFromTree(encodingTree);
        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {
                ds.Write(byteArray);
                ds.Position = 0;

                decoder.ReadOddSizedEncodedData(ds, (uint)byteArray.Length);
            }
        }

        Dictionary<string, int> lookupMap = new(encodingResult.EncodedTextPositions.Select(t => KeyValuePair.Create(t.Identifier, t.Position)).ToList());
        List<string> decoded = new();
        foreach (string originalText in texts)
        {
            int bitOffset = lookupMap[originalText];
            string readFromDecoder = decoder.ReadHuffmanEncodedString(bitOffset);

            decoded.Add(readFromDecoder);
        }

        int sizeModuloOp = byteArray.Length & 3;
        Assert.Multiple(() =>
        {
            Assert.That(decoded, Has.Count.EqualTo(texts.Length), "Dedoded number of texts does not match input number of texts!");
            Assert.That(decoded.ToArray(), Is.EqualTo(texts), "Dedoded texts do not match given texts to encode!");
            Assert.That(sizeModuloOp, Is.EqualTo(0), "Encoded byte array is not divisible by 4 without rest!");
        });
    }

    [Test]
    public void TestAllInOneMethod()
    {
        string[] texts = { "I'm a mog, half man, half dog", "I'm my own best friend!", "Oh yes, now they are small and cute and cuddly", " and next they suddenly have teeth", " and there is a thousand of them" };

        var encodingResult = HuffmanEncoder.Encode(texts);
        byte[] byteArray = encodingResult.EncodedTexts;

        HuffmanDecoder decoder = CreateDecoderFromTree(encodingResult.EncodingTree);
        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {
                ds.Write(byteArray);
                ds.Position = 0;

                decoder.ReadOddSizedEncodedData(ds, (uint)byteArray.Length);
            }
        }

        var lookupMap = encodingResult.GetTextPositionsDictionary();
        List<string> decoded = new();
        foreach (string originalText in texts)
        {
            int bitOffset = lookupMap[originalText];
            string readFromDecoder = decoder.ReadHuffmanEncodedString(bitOffset);

            decoded.Add(readFromDecoder);
        }

        int sizeModuloOp = byteArray.Length & 3;
        Assert.Multiple(() =>
        {
            Assert.That(decoded, Has.Count.EqualTo(texts.Length), "Dedoded number of texts does not match input number of texts!");
            Assert.That(decoded.ToArray(), Is.EqualTo(texts), "Dedoded texts do not match given texts to encode!");
            Assert.That(sizeModuloOp, Is.EqualTo(0), "Encoded byte array is not divisible by 4 without rest, even though padding should be used!");
        });
    }

    [TestCase(0, false, ExpectedResult = 0)]
    [TestCase(0, true, ExpectedResult = 0)]
    [TestCase(1, false, ExpectedResult = 1)]
    [TestCase(1, true, ExpectedResult = 4)]
    [TestCase(8, false, ExpectedResult = 1)]
    [TestCase(8, true, ExpectedResult = 4)]
    [TestCase(9, false, ExpectedResult = 2)]
    [TestCase(9, true, ExpectedResult = 4)]
    [TestCase(16, false, ExpectedResult = 2)]
    [TestCase(16, true, ExpectedResult = 4)]
    [TestCase(17, false, ExpectedResult = 3)]
    [TestCase(17, true, ExpectedResult = 4)]
    [TestCase(24, false, ExpectedResult = 3)]
    [TestCase(24, true, ExpectedResult = 4)]
    [TestCase(25, false, ExpectedResult = 4)]
    [TestCase(25, true, ExpectedResult = 4)]
    [TestCase(32, false, ExpectedResult = 4)]
    [TestCase(32, true, ExpectedResult = 4)]
    [TestCase(33, false, ExpectedResult = 5)]
    [TestCase(33, true, ExpectedResult = 8)]
    [TestCase(2400, false, ExpectedResult = 300)]
    [TestCase(2400, true, ExpectedResult = 300)]
    public int TestPadding(int bitSize, bool usePadding)
    {
        return HuffmanEncoder.GetDataLengthInBytes(bitSize, usePadding);
    }

    [TestCase("FrostySdkTest.TestData.original_huffman", Endian.Big, ExpectedResult = "win32/content/common/configs/bundles/careermodestory_sba")]
    [TestCase("FrostySdkTest.TestData.new_huffman", Endian.Little, ExpectedResult = "win32/content/cinematic/scenes/livinghubs/f22_pap_lh_ll/f22_pap_lh_ll_set_sublevel")]
    public String TestReadFirstEntryFromFile(string testFilePath, Endian encodedTestEndian)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream(testFilePath))
        {

            if (stream == null)
            {
                return "Cannot read resource file!";
            }

            // same for both test files
            uint textLengthInBytes = 300;

            HuffmanDecoder decoder = new();
            using (DataStream ds = new(stream))
            {

                // new huffman was created when writing the string data ignored endianness and always used little endian.
                decoder.ReadOddSizedEncodedData(ds, textLengthInBytes, encodedTestEndian);

                ds.Position = textLengthInBytes;

                uint numberOfTreeNodes = (uint)(ds.Length - ds.Position) / 4;

                // both test files were created with big endian as setting.
                decoder.ReadHuffmanTable(ds, numberOfTreeNodes, Endian.Big);
            }

            string firstEntry = decoder.ReadHuffmanEncodedString(0);

            return firstEntry;
        }
    }
}