using Frosty.Sdk.IO;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Frosty.Sdk.Utils;

/// <summary>
/// A <see cref="HuffmanNode"/> with additional integer for how many times this node was encountered in the data to encode. This is used to construct the huffman tree.
/// </summary>
internal class HuffManConstructionNode : HuffmanNode, IComparable<HuffManConstructionNode>
{
    public int Occurrences { get; set; }

    public new HuffManConstructionNode? Left { get; private set; }

    public new HuffManConstructionNode? Right { get; private set; }

    public HuffManConstructionNode()
    {
        Occurrences = 0;
    }

    public HuffManConstructionNode(char inValueChar, int inOccurrences)
    {
        Value = ~(uint)inValueChar;
        Occurrences = inOccurrences;
    }

    public void SetLeftNode(HuffManConstructionNode leftNode)
    {
        base.SetLeftNode(leftNode);
        Left = leftNode;
        Occurrences += leftNode.Occurrences;
    }

    public void SetRightNode(HuffManConstructionNode rightNode)
    {
        base.SetRightNode(rightNode);
        Right = rightNode;
        Occurrences += rightNode.Occurrences;
    }

    public int CompareTo(HuffManConstructionNode? other)
    {
        int cmp = Occurrences.CompareTo(other?.Occurrences);
        if (cmp == 0)
        {
            cmp = GetRemainingDepth().CompareTo(other?.GetRemainingDepth());
        }
        return cmp;
    }

    private int GetRemainingDepth()
    {
        int ld = Left?.GetRemainingDepth() ?? 0;
        int rd = Right?.GetRemainingDepth() ?? 0;

        return Math.Max(ld, rd);
    }
}

#region Return type classes

/// <summary>
/// Just a tuple of an identifier and a position with clearer naming than a Tuple.
/// This is used as part of the return value of the encoding method. The identifier is to be the identifier of a string or text, with the coupled position being the bit offset in the encoded byte array.
/// </summary>
/// <typeparam name="T">The type of identifier used, might a simple uint or a complex type.</typeparam>
public class IdentifierPositionTuple<T>
{
    /// <summary>
    /// The identifier of an encoded string.
    /// </summary>
    public T Identifier { get; private set; }

    /// <summary>
    /// The position of the encoded string.
    /// </summary>
    public int Position { get; private set; }

    public IdentifierPositionTuple(T inIdentifier, int inPosition)
    {
        Identifier = inIdentifier;
        Position = inPosition;
    }
}

/// <summary>
/// Return value of the encoding function. This contains the encoded texts as byte array, as well as the list of <see cref="IdentifierPositionTuple{T}"/> that detail which text is at what bit offset inside the array.
/// <list type="bullet">
/// <item>
/// <description><c>HuffmanEncodedTextArray.EncodedTexts</c> returns the given strings encoded to a byte array..</description>
/// </item>
/// <item>
/// <description><c>HuffmanEncodedTextArray.EncodedTextPositions</c> returns the text keys an their bit offsets in the <c>EncodedTexts</c> in the form of a list of tuples for use in iterations.</description>
/// </item>
/// <item>
/// <description><c>HuffmanEncodedTextArray.GetTextPositionsDictionary()</c> returns a dictionary with the the same data as above.</description>
/// </item>
/// </list>
/// </summary>
/// <typeparam name="T">The type of identifier used for the texts.</typeparam>
public class HuffmanEncodedTextArray<T> where T : notnull
{
    /// <summary>
    /// The list of string identifiers, and the bit position of the text for the identifier inside the <see cref="EncodedTexts"/> byte array or <see cref="EncodedTestsAsBools"/> list.
    /// </summary>
    public IList<IdentifierPositionTuple<T>> EncodedTextPositions { get; private set; }

    /// <summary>
    /// Returns the string as encoded bools.
    /// </summary>
    public IList<bool> EncodedTestsAsBools { get; private set; }

    /// <summary>
    /// All the encoded texts as single byte array. This only exists if the result was created with set encoding!
    /// <see cref="HuffmanEncoder.GetByteArrayForBoolList(IList{bool}, Endian, bool)"/> to get the wanted byte representation for <see cref="EncodedTestsAsBools"/> or call <see cref="CreateEncodedTexts(Endian, bool)"/>
    /// </summary>
    public byte[]? EncodedTexts { get; internal set; }

    // Dictionary representation of EncodedTextPositions created when first requested.
    private Dictionary<T, int> m_positionsDictionary = null;

    public HuffmanEncodedTextArray(IList<IdentifierPositionTuple<T>> inEncodedTextPositions, IList<bool> inEncodedTestsAsBools)
    {
        EncodedTextPositions = inEncodedTextPositions;
        EncodedTestsAsBools = inEncodedTestsAsBools;
    }

    /// <summary>
    /// Copy Constructor
    /// </summary>
    /// <param name="inOriginal"></param>
    protected HuffmanEncodedTextArray(HuffmanEncodedTextArray<T> inOriginal)
    {
        EncodedTextPositions = inOriginal.EncodedTextPositions;
        EncodedTestsAsBools = inOriginal.EncodedTestsAsBools;
        EncodedTexts = inOriginal.EncodedTexts;
        m_positionsDictionary = inOriginal.m_positionsDictionary;
    }

    /// <summary>
    /// Returns the values of the <see cref="EncodedTextPositions"/> as dictionary for easier lookup outside of loop queries.
    /// </summary>
    /// <returns>Dictionary with string identifiers and their bit offset in the <see cref="EncodedTexts"/> or <see cref="EncodedTestsAsBools"/> </returns>
    public Dictionary<T, int> GetTextPositionsDictionary()
    {
        if (m_positionsDictionary == null)
        {
            m_positionsDictionary = new Dictionary<T, int>(this.EncodedTextPositions.Select(entry => KeyValuePair.Create(entry.Identifier, entry.Position)).ToList());
        }
        return m_positionsDictionary;
    }

    /// <summary>
    /// Creates a new value for <see cref="EncodedTexts"/> based on the given inputs, replacing any previous set one and returning the new value.
    /// <seealso cref="HuffmanEncoder.GetByteArrayForBoolList(IList{bool}, Endian, bool)"/>
    /// </summary>
    /// <param name="endian">The endian to use</param>
    /// <param name="usePadding">Whether or not to padd the byte array.</param>
    /// <returns>The non null value of <see cref="EncodedTexts"/></returns>
    public byte[] CreateEncodedTexts(Endian endian = Endian.Little, bool usePadding = true)
    {
        EncodedTexts = HuffmanEncoder.GetByteArrayForBoolList(EncodedTestsAsBools, endian, usePadding);
        return EncodedTexts;
    }
}

/// <summary>
/// <list type="bullet">
/// <item>
/// <description><c>EncodingResult.EncodingTree</c> returns the huffman tree used for encoding in the form of an integer list.</description>
/// </item>
/// <item>
/// <description><c>EncodingResult.EncodedTexts</c> returns the given strings encoded to a byte array..</description>
/// </item>
/// <item>
/// <description><c>EncodingResult.GetTextPositionsDictionary()</c> returns a dictionary with the given strings as keys and their bit offsets in the <c>EncodedTexts</c>.</description>
/// </item>
/// <item>
/// <description><c>EncodingResult.EncodedTextPositions</c> returns the same data as above in the form of a list of tuples for use in iterations.</description>
/// </item>
/// </list>
/// </summary>
public class EncodingResult : HuffmanEncodedTextArray<string>
{
    /// <summary>
    /// The encoding tree in the form of an integer list.
    /// </summary>
    public IList<uint> EncodingTree { get; private set; }

    /// <summary>
    /// Creates a new result object from an existing HuffmanEncodedTextArray with additional encoding tree information.
    /// </summary>
    /// <param name="inEncodedTextArray"> the original result</param>
    /// <param name="inEncodingTree"> the encoding tree</param>
    public EncodingResult(HuffmanEncodedTextArray<string> inEncodedTextArray, IList<uint> inEncodingTree)
        : base(inEncodedTextArray)
    {
        EncodingTree = inEncodingTree;
    }
}

#endregion

/// <summary>
/// HuffmanEncoder can be used to encode given strings into a byte array.
/// Usage:
/// Either call <see cref="HuffmanEncoder.Encode(IEnumerable{string})"/> directly with the strings to encode.
/// Or if more control is required, call <see cref="HuffmanEncoder.BuildHuffmanEncodingTree(IEnumerable{string})"> with a representation of the strings to encode to build the encoding tree. After that call <see cref="HuffmanEncoder.EncodeTexts{T}(IEnumerable{Tuple{T, string}}, bool, bool)"/> to get the encoding result.
/// </summary>
public class HuffmanEncoder
{
    /// <summary>
    /// the character encoding in dictionary form.
    /// </summary>
    private IDictionary<char, IList<bool>>? m_characterEncoding;

    /// <summary>
    /// Helper method to get the encoded texts and the encoding tree all in one go.
    /// The encoded texts, the encoding, and the individual texts offsets can be retrieved from the <see cref="EncodingResult"/>.
    /// <list type="bullet">
    /// <item>
    /// <description><c>EncodingResult.EncodingTree</c> returns the huffman tree used for encoding in the form of an integer list.</description>
    /// </item>
    /// <item>
    /// <description><c>EncodingResult.EncodedTexts</c> returns the given strings encoded to a byte array..</description>
    /// </item>
    /// <item>
    /// <description><c>EncodingResult.GetTextPositionsDictionary()</c> returns a dictionary with the given strings as keys and their bit offsets in the <c>EncodedTexts</c>.</description>
    /// </item>
    /// <item>
    /// <description><c>EncodingResult.EncodedTextPositions</c> returns the same data as above in the form of a list of tuples for use in iterations.</description>
    /// </item>
    /// </list>
    /// Note that this method always uses little endinan for the text data.
    /// </summary>
    /// <param name="texts">The texts to encode</param>
    /// <returns>EncodingResult with the data.</returns>
    public static EncodingResult Encode(IEnumerable<string> texts)
    {

        ISet<String> nonNullUniqueStrings = new HashSet<String>(texts);

        HuffmanEncoder encoder = new();
        IList<uint> tree = encoder.BuildHuffmanEncodingTree(nonNullUniqueStrings);

        var encodedTexts = encoder.EncodeTexts(nonNullUniqueStrings.Select(
            x => new Tuple<string, string>(x, x)).ToList(), Endian.Little, false);

        return new EncodingResult(encodedTexts, tree);
    }


    /// <summary>
    /// Computes the necessary length in bytes for the given number of bits with optionally added padding.
    /// </summary>
    /// <param name="numberOfBits">Number of bits to turn into a byte array</param>
    /// <param name="usePadding">Whether or not to use padding to get to the next 32 bits or 4 byte size</param>
    /// <returns>the number of necessary bytes to store the given number of bits with the given padding option</returns>
    public static int GetDataLengthInBytes(int numberOfBits, bool usePadding)
    {

        int byteSize = Math.DivRem(numberOfBits, 8, out int remainder);
        if (remainder != 0)
        {
            byteSize++;
        }

        if (usePadding)
        {
            int paddingLength = 4 - byteSize & 3;
            // paddingLength is computed as modulo 4 operation, just uing bitwise and here.
            byteSize += paddingLength;
        }

        return byteSize;
    }

    /// <summary>
    /// Returns the byte array for the given bool list in the specified endianness and padding. Some things to note:
    /// <list type="bullet">
    /// <item>The byte array returns the bools as 32 bit integer blocks, the endianness is applied to this integer representation.</item>
    /// <item>Endianess behabiour overwrites padding behaviour. The default is a little endian byte array, if this is changed to big endian than padding is applied regardless of the argument.</item>
    /// </list>
    /// </summary>
    /// <param name="encodedTestsAsBools">The list of bools to convert to byte array.</param>
    /// <param name="endian">The endianness to apply to the (integer) representation of the bool array.</param>
    /// <param name="usePadding">Whether or not to use padd the result to get to the next 4 bytes length. If big endian is used, then the result is always padded.</param>
    /// <returns>A byte array with the given boolean list as byte representation</returns>
    public static byte[] GetByteArrayForBoolList(IList<bool> encodedTestsAsBools, Endian endian = Endian.Little, bool usePadding = true)
    {

        // BitArray is always little endian by default!
        if (endian != Endian.Big)
        {
            return GetByteArrayForBoolList0(encodedTestsAsBools, usePadding);
        }

        // change of endianness forces padding:
        byte[] littleEndianByteArray = GetByteArrayForBoolList0(encodedTestsAsBools, true);

        List<byte> reverseList = new(littleEndianByteArray.Length);

        int index = 0;
        while (index + 4 < littleEndianByteArray.Length)
        {
            uint number = BitConverter.ToUInt32(littleEndianByteArray, index);
            uint reverseNumber = BinaryPrimitives.ReverseEndianness(number);

            byte[] reverseBytes = BitConverter.GetBytes(reverseNumber);
            reverseList.AddRange(reverseBytes);

            index += 4;
        }

        // this should no longer be necessary, when calling padding before
        if (index < littleEndianByteArray.Length)
        {
            byte[] intermediate = new byte[4];

            for (int i = 0; i < 4 && index < littleEndianByteArray.Length; i++)
            {
                intermediate[i] = littleEndianByteArray[index];
                index++;
            }
            uint number = BitConverter.ToUInt32(intermediate);
            uint reverseNumber = BinaryPrimitives.ReverseEndianness(number);

            byte[] reverseBytes = BitConverter.GetBytes(reverseNumber);
            reverseList.AddRange(reverseBytes);
        }

        return reverseList.ToArray();
    }

    /// <summary>
    /// Uses the given input to construct the huffman encoding table (or tree). The created encoding includes end delimiter character (char 0x0) with a number of occurrences of the number of given strings.
    /// Also temporarily stores the given texts to use them in other methods if the need arises.
    /// Returns the uint representation of the huffman encoding.
    /// Note that this method is quite a bit slower than calling <see cref="EncodeTexts{T}(IEnumerable{Tuple{T, string}}, bool, bool)"/>, but is null and duplicate save as well as padding the result data.
    /// </summary>
    /// <param name="texts">The texts to encode, or a suitable approximation of the characters appearances.</param>
    /// <returns>The list of huffman node values in the order they should be written.</returns>
    public IList<uint> BuildHuffmanEncodingTree(IEnumerable<string> texts)
    {
        IList<string> strings = texts.ToList();
        HuffManConstructionNode rootNode = CalculateHuffmanEncoding(strings);

        IList<HuffmanNode> encodingNodes = GetNodeListToWrite(rootNode);
        m_characterEncoding = GetCharEncoding(encodingNodes);

        return encodingNodes.Select(node => node.Value).ToList();
    }

    /// <summary>
    /// Encodes the given text into a bool values, using the encoding set previously by <see cref="HuffmanEncoder.BuildHuffmanEncodingTree(IEnumerable{string})"/>.
    /// The result bool list can e.g., be put into an BitArray by calling <c>new BitArray(resultList.ToArray())</c>. From there it can be copied into byte arrays or similar.
    /// </summary>
    /// <param name="text">A single text to encode.</param>
    /// <param name="includeEndDelimeter">Whether or not the encoding for a 0x0 character should be added to the end of the text.</param>
    /// <returns>the bool representation of the encoded text.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If a character or symbol to encode was not found in the dictionary</exception>
    /// <exception cref="System.InvalidOperationException">If no encoding has been created yet.</exception>
    public IList<bool> EncodeText(string text, bool includeEndDelimeter = true)
    {
        CheckEncodingExists();
        return GetEncodedText(text, m_characterEncoding!, includeEndDelimeter);
    }

    /// <summary>
    /// Encodes the given String using the previously created Huffman Encoding from <see cref="HuffmanEncoder.BuildHuffmanEncodingTree(IEnumerable{string})"/> and returns the created byte array together with the list of text identifiers and their bit offsets in the byte array.
    /// NOTE: Changing the encoding to big endian might invalidate the assigned positions for the text in the returned result!
    /// <seealso cref="EncodeTextsToBool{T}(IEnumerable{Tuple{T, string}}, bool)"/>
    /// <seealso cref="GetByteArrayForBoolList(IList{bool}, Endian, bool)"/>
    /// </summary>
    /// <typeparam name="T">The type of identifier to use for the texts. Might be a uint id/counter/hashvalue or complex type. Has to be unique per string and not null</typeparam>
    /// <param name="textsPerIdentifier">The tuples of string identifiers and the strings to encode.</param>
    /// <param name="endian"> The endian type to use for creating the byte array in the returned result object.
    /// If no endian is given then the result will not include the byte array and will be the same as created by <c>EncodeTextsToBool</c>! You can call <see cref="GetByteArrayForBoolList(IList{bool}, Endian, bool)"/> with the wanted settings later.
    /// Note that the given endian overwrites any behaviour from <c>usePadding</c>. If the given encoding is not null but different from the system setting then the result is always padded.
    /// Note furthermore that big endian settings migh mean the returned bit offsets for texts in the result will no longer be valid on the target system!
    /// </param>
    /// <param name="compressResults">If true, then this method tries to reuse already compiled string encodings and produced bit segments, so the returned byte array might be shorter. Defaults to false.</param>
    /// <param name"usePadding">Whether to use padding to get to increase the data byte array size so it is a multiple of 4. I.e., multiples of 32 bit. This behaviur is overwritten by the given <c>endian</c>. Endians different than the system default will always be padded.</param>
    /// <returns>An instance of <see cref="HuffmanEncodedTextArray{T}"/> with the byte array of the encoded texts, and a list of the given text identifiers and their bit position inside the byte array. The list has the same ordering as the given input to this method. </returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If a character or symbol to encode was not found in the dictionary</exception>
    /// <exception cref="System.InvalidOperationException">If no encoding has been created yet.</exception>
    public HuffmanEncodedTextArray<T> EncodeTexts<T>(IEnumerable<Tuple<T, string>> textsPerIdentifier, Endian? endian, bool compressResults = false, bool usePadding = true) where T : notnull
    {

        HuffmanEncodedTextArray<T> byteListResult = EncodeTextsToBool(textsPerIdentifier, compressResults);

        if (endian != null)
        {
            byte[] encodedToBytes = GetByteArrayForBoolList(byteListResult.EncodedTestsAsBools, endian ?? Endian.Little, usePadding);
            byteListResult.EncodedTexts = encodedToBytes;
        }

        return byteListResult;
    }

    /// <summary>
    /// Encodes the given String using the previously created Huffman Encoding from <see cref="HuffmanEncoder.BuildHuffmanEncodingTree(IEnumerable{string})"/> and returns the List of bools for the encoded texts.
    /// Note that this method replaces null strings with empty strings.
    /// NOTE: The returned <see cref="HuffmanEncodedTextArray{T}"/> will not have the byte array set, only the bool list!
    /// </summary>
    /// <typeparam name="T">The type of identifier to use for the texts. Might be a uint id/counter/hashvalue or complex type. Has to be unique per string and not null</typeparam>
    /// <param name="textsPerIdentifier">The tuples of string identifiers and the strings to encode.</param>
    /// <param name="compressResults">If true, then this method tries to reuse already compiled string encodings and produced bit segments, so the returned byte array might be shorter. Defaults to false.</param>
    /// <returns>An instance of <see cref="HuffmanEncodedTextArray{T}"/> with the bool list of the encoded texts, and a list of the given text identifiers and their bit position inside the list. The list has the same ordering as the given input to this method.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If a character or symbol to encode was not found in the dictionary</exception>
    /// <exception cref="System.InvalidOperationException">If no encoding has been created yet.</exception>
    public HuffmanEncodedTextArray<T> EncodeTextsToBool<T>(IEnumerable<Tuple<T, string>> textsPerIdentifier, bool compressResults = false) where T : notnull
    {
        CheckEncodingExists();

        List<IdentifierPositionTuple<T>> positionsOfStrings = new();
        List<bool> encodedTextBools = new();

        Dictionary<string, int> alreadyEncodedTextPositions = new();

        foreach (var textWithIdentifier in textsPerIdentifier)
        {

            T textIdentifier = textWithIdentifier.Item1;
            string text = textWithIdentifier.Item2 ?? "";
            int position = encodedTextBools.Count;

            bool encodeText = true;
            if (compressResults)
            {
                bool exists = alreadyEncodedTextPositions.TryGetValue(text, out int existingPos);
                if (!exists)
                {
                    alreadyEncodedTextPositions[text] = position;
                }
                else
                {
                    position = existingPos;
                    encodeText = false;
                }
            }

            positionsOfStrings.Add(new IdentifierPositionTuple<T>(textIdentifier, position));

            if (encodeText)
            {
                encodedTextBools.AddRange(GetEncodedText(text, m_characterEncoding!, true));
            }
        }

        return new HuffmanEncodedTextArray<T>(positionsOfStrings, encodedTextBools);
    }

    /// <summary>
    /// Resets instance variables.
    /// </summary>
    public void Dispose()
    {
        m_characterEncoding = null;
    }

    /// <summary>
    /// Calculates the huffman encoding for the given texts, and returns the root node of the resulting tree.
    /// </summary>
    /// <param name="texts"></param>
    /// <returns>Huffman root node.</returns>
    private static HuffManConstructionNode CalculateHuffmanEncoding(IList<string> texts)
    {
        // get set of chars and their number of occurrences...
        Dictionary<char, int> charNumbers = new();
        foreach (string text in texts)
        {
            foreach (char c in text)
            {
                if (charNumbers.TryGetValue(c, out int occurences))
                {
                    charNumbers[c] = ++occurences;
                }
                else
                {
                    charNumbers[c] = 1;
                }
            }
        }

        // add the text delimiter:
        char delimiter = (char)0x0;
        charNumbers[delimiter] = texts.Count;

        List<HuffManConstructionNode> nodeList = new();
        foreach (var entry in charNumbers)
        {
            nodeList.Add(new HuffManConstructionNode(entry.Key, entry.Value));
        }

        uint nodeValue = 0;
        while (nodeList.Count > 1)
        {

            nodeList.Sort();

            HuffManConstructionNode left = nodeList[0];
            HuffManConstructionNode right = nodeList[1];

            nodeList.RemoveRange(0, 2);

            HuffManConstructionNode composite = new()
            {
                Value = nodeValue++,
            };
            composite.SetLeftNode(left);
            composite.SetRightNode(right);

            nodeList.Add(composite);
        }

        return nodeList[0];
    }

    /// <summary>
    /// Returns a dictionary of characters to their encoded bit representation.
    /// </summary>
    /// <param name="encodingNodes">The (limited) list of Huffman code nodes used.</param>
    /// <returns>Dictionary of char - code values</returns>
    private static IDictionary<char, IList<bool>> GetCharEncoding(IList<HuffmanNode> encodingNodes)
    {

        Dictionary<char, IList<bool>> charEncodings = new();

        foreach (HuffmanNode node in encodingNodes)
        {
            if (node.Left == null && node.Right == null)
            {
                char c = node.Letter;
                IList<bool> path = GetCharEncodingRecursive(node);

                charEncodings.Add(c, path);
            }
        }
        return charEncodings;
    }

    /// <summary>
    /// Recalculates the nodelist to write to the resource based on the root node.
    /// This method returns a flat list or huffman nodes, that do not include the given root node.
    /// </summary>
    /// <param name="rootNode">the root node of the tree to flatten into a single list.</param>
    /// <returns>list of nodes in the order to write</returns>
    private static IList<HuffmanNode> GetNodeListToWrite(HuffmanNode? rootNode)
    {
        List<HuffmanNode> nodesSansRoot = new();

        if (rootNode == null)
        {
            return nodesSansRoot;
        }

        // get all branches
        List<HuffmanNode> branches = GetAllBranchNodes(new List<HuffmanNode>() { rootNode });

        // sort branches by their value, so that the write out can happen in the correct order
        branches.Sort();

        // add all the children in the order of their parent's value
        foreach (HuffmanNode branch in branches)
        {
            nodesSansRoot.Add(branch.Left!);
            nodesSansRoot.Add(branch.Right!);
        }

        return nodesSansRoot;
    }

    private static List<HuffmanNode> GetAllBranchNodes(List<HuffmanNode> currentNodes)
    {
        List<HuffmanNode> branchNodes = new();

        foreach (HuffmanNode currentNode in currentNodes)
        {
            if (currentNode.Left is not null && currentNode.Right is not null)
            {
                branchNodes.Add(currentNode);
                branchNodes.AddRange(
                    GetAllBranchNodes(new List<HuffmanNode> { currentNode.Left, currentNode.Right }));
            }
        }

        return branchNodes;
    }

    /// <summary>
    /// Return the encoding for the given node as path in the tree.
    /// </summary>
    /// <param name="node">The node for which to find the encoding.</param>
    /// <returns>the encoding as list of booleans.</returns>
    private static IList<bool> GetCharEncodingRecursive(HuffmanNode node)
    {
        HuffmanNode? parent = node.Parent;
        if (parent == null)
        {
            return new List<bool>();
        }

        IList<bool> encoding = GetCharEncodingRecursive(parent);

        if (node == parent.Left)
        {
            encoding.Add(false);
        }
        else if (node == parent.Right)
        {
            encoding.Add(true);
        }
        else
        {
            throw new InvalidOperationException(
                $"Trying to find encoding for node <{node}> failed due to incorrectly setup encoding tree!");
        }

        return encoding;
    }

    /// <summary>
    /// Returns the bit encoded text as list of booleans.
    /// The end of the text is marked with the delimiter character 0x0 / huffman node value = uint.MaxValue.
    /// </summary>
    /// <param name="toEncode">The text to encode, given as char array or similar char enumerable</param>
    /// <param name="charEncoding">The character encoding to use for the text.</param>
    /// <param name="includeEndDelimeter">Whether or not to include the end delimiter in the returned encoding.</param>
    /// <returns>The encoded text.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If a character or symbol to encode was not found in the dictionary</exception>
    private static IList<bool> GetEncodedText(IEnumerable<char> toEncode, IDictionary<char, IList<bool>> charEncoding, bool includeEndDelimeter)
    {
        List<bool> encodedText = new();

        foreach (char c in toEncode)
        {
            encodedText.AddRange(TryGetCharacterEncoding(c, charEncoding));
        }

        if (includeEndDelimeter)
        {
            char delimiter = (char)0x0;
            encodedText.AddRange(TryGetCharacterEncoding(delimiter, charEncoding));
        }

        return encodedText;
    }

    private static IList<bool> TryGetCharacterEncoding(char c, IDictionary<char, IList<bool>> charEncoding)
    {

        bool found = charEncoding.TryGetValue(c, out IList<bool>? encodedChar);
        if (found)
        {
            return encodedChar!;
        }

        if (c == 0x0)
        {
            throw new KeyNotFoundException("Encoding does not contain mapping for end delimiter!");
        }

        string errorMessage = $"Encoding does not contain a mapping for symbol of value {(int)c}: '{c}'!";
        throw new KeyNotFoundException(errorMessage);
    }

    // Returns the byte array for the given bool list. Does not take encoding into account!
    // According to the BitArray docs the result is always in little endian.
    private static byte[] GetByteArrayForBoolList0(IList<bool> encodedTestsAsBools, bool usePadding)
    {
        int byteSize = GetDataLengthInBytes(encodedTestsAsBools.Count, usePadding);

        BitArray ba = new(encodedTestsAsBools.ToArray());

        byte[] byteArray = new byte[byteSize];

        ba.CopyTo(byteArray, 0);

        return byteArray;
    }

    /// <summary>
    /// Asserts that an encoding has been created.
    /// </summary>
    /// <exception cref="InvalidOperationException">If no encoding exists.</exception>
    private void CheckEncodingExists()
    {
        if (m_characterEncoding == null)
        {
            throw new InvalidOperationException("Cannot encode texts before the Huffman tree was built! Call 'BuildHuffmanEncodingTree' on the encoder before attempting to encode a string!");
        }
    }

}