using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;
using static Frosty.Sdk.Utils.EncodingResult;

namespace FrostySdkTest.Utils;

public class HuffmanEncodingTests
{
    private static readonly object[] s_encodingDecodingTestValues =
    {
        new object[] { false, 25 },
        new object[] { true, 22 }
    };

    /// <summary>
    /// Tests the encoding and decoding of some test strings. The argument source once encodes the strings with reusing existing entries, and once without, leading to different result byte lengths.
    /// </summary>
    /// <param name="compressResults"></param>
    /// <param name="encodedByteSize"></param>
    [TestCaseSource(nameof(s_encodingDecodingTestValues))]
    public void TestEncodingDecoding(bool compressResults, int encodedByteSize)
    {
        string[] texts = { "These are ", "", "some ", "Test Texts", " for tests ", "some ", " these are" };

        HuffmanEncoder encoder = new();

        var encodingTree = encoder.BuildHuffmanEncodingTree(texts);

        int i = 0;
        var input = texts.Select(x => new Tuple<int, string>(i++, x)).ToList();

        var encodingResult = encoder.EncodeTexts(input, compressResults);
        Assert.Multiple(() =>
        {
            Assert.That(encodingResult.EncodedTexts, Has.Length.EqualTo(encodedByteSize));
            Assert.That(encodingResult.EncodedTextPositions, Has.Count.EqualTo(texts.Length));
        });
        HuffmanDecoder decoder = CreateDecoderFromTree(encodingTree);

        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {

                var byteArray = encodingResult.EncodedTexts;

                ds.Write(byteArray);
                ds.Position = 0;

                decoder.ReadOddSizedEncodedData(ds, (uint)byteArray.Length);
            }
        }

        List<string> decoded = new();

        foreach (var textId in encodingResult.EncodedTextPositions)
        {
            string decodedText = decoder.ReadHuffmanEncodedString(textId.Position);

            decoded.Add(decodedText);

            Assert.That(decodedText, Is.EqualTo(texts[textId.Identifier]));
        }

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Has.Count.EqualTo(texts.Length));
            Assert.That(decoded.ToArray(), Is.EqualTo(texts));
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
        string[] texts = {"Some ", "more ", "Text that might be ", "stored together ", "or whatever ", "these are only ", "for test usage"};

        HuffmanEncoder encoder = new();
        var encodingTree = encoder.BuildHuffmanEncodingTree(texts);

        HuffmanEncodedTextArray<string> encodingResult = encoder.EncodeTexts(texts.Select(
            x => new Tuple<string, string>(x,x)).ToList(), false);

        HuffmanDecoder decoder = CreateDecoderFromTree(encodingTree);

        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {

                var byteArray = encodingResult.EncodedTexts;

                ds.Write(byteArray);
                ds.Position = 0;

                decoder.ReadOddSizedEncodedData(ds, (uint)byteArray.Length);
            }
        }

        Dictionary<string, int> lookupMap = new (encodingResult.EncodedTextPositions.Select(t => KeyValuePair.Create(t.Identifier, t.Position)).ToList());

        foreach(string originalText in texts)
        {
            int bitOffset = lookupMap[originalText];
            string readFromDecoder = decoder.ReadHuffmanEncodedString(bitOffset);

            Assert.AreEqual(originalText, readFromDecoder);
        }
    }

    [Test]
    public void TestAllInOneMethod()
    {
        var texts = new List<string> { "I'm a mog, half man, half dog", "I'm my own best friend!", "Oh yes, now they are small and cute and cuddly", " and next they suddenly have teeth", " and there is a thousand of them" };

        var encodingResult = HuffmanEncoder.Encode(texts);

        HuffmanDecoder decoder = CreateDecoderFromTree(encodingResult.EncodingTree);
        using (MemoryStream stream = new())
        {
            using (DataStream ds = new(stream))
            {

                var byteArray = encodingResult.EncodedTexts;

                ds.Write(byteArray);
                ds.Position = 0;

                decoder.ReadOddSizedEncodedData(ds, (uint)byteArray.Length);
            }
        }

        var lookupMap = encodingResult.GetTextPositionsDictionary();
        foreach (string originalText in texts)
        {
            int bitOffset = lookupMap[originalText];
            string readFromDecoder = decoder.ReadHuffmanEncodedString(bitOffset);

            Assert.AreEqual(originalText, readFromDecoder);
        }
    }
}