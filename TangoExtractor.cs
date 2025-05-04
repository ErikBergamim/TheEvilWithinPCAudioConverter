using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;

namespace The_Evil_Within_Audio_Manager
{
    
    public class TangoExtractor
    {
        private readonly Action<string> _logCallback;
        private readonly Action<int> _updateProgressCallback;
        private readonly Action<int> _setMaxProgressCallback;

        public TangoExtractor(
            Action<string> logCallback,
            Action<int> updateProgressCallback,
            Action<int> setMaxProgressCallback)
        {
            _logCallback = logCallback;
            _updateProgressCallback = updateProgressCallback;
            _setMaxProgressCallback = setMaxProgressCallback;
        }
        public bool canConvert = false;
        public void ExtractStreamed(string streamedPath)
        {
            if (!streamedPath.EndsWith(".streamed", StringComparison.OrdinalIgnoreCase))
            {
                Log("The selected file is not a .streamed file");
                return;
            }
            string directory = Path.GetDirectoryName(streamedPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(streamedPath);

            string baseNameWithoutSuffix = baseName;
            if (baseName.EndsWith("_en", StringComparison.OrdinalIgnoreCase))
            {
                baseNameWithoutSuffix = baseName.Substring(0, baseName.Length - 3);
            }

            var tangoresourceFiles = Directory.GetFiles(directory, "*.tangoresource");
            foreach (var file in tangoresourceFiles)
            {
                Log($"- {Path.GetFileName(file)}");
            }

            string resourcePath = Path.Combine(directory, $"{baseNameWithoutSuffix}.tangoresource");
            string resourcePathWithEn = Path.Combine(directory, $"{baseName}.tangoresource");
            string finalResourcePath = null;
            if (File.Exists(resourcePath))
            {
                finalResourcePath = resourcePath;
                Log($"Matching .tangoresource found: {finalResourcePath}");
            }
            else if (File.Exists(resourcePathWithEn))
            {
                finalResourcePath = resourcePathWithEn;
                Log($"✓ Alternative .tangoresource found: {finalResourcePath}");
            }
            else
            {
                var possibleMatch = tangoresourceFiles.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).IndexOf(baseNameWithoutSuffix,
                    StringComparison.OrdinalIgnoreCase) >= 0);

                if (possibleMatch != null)
                {
                    finalResourcePath = possibleMatch;
                    Log($"Partial match found: {Path.GetFileName(finalResourcePath)}");
                }
                else
                {
                    Log($"No matching .tangoresource file found.");
                }
            }
            string outputDir = Path.Combine(directory, baseName + "_extracted");
            Directory.CreateDirectory(outputDir);
            Extract(streamedPath, outputDir);
            Log($".streamed file extraction completed!");
            if (!string.IsNullOrEmpty(finalResourcePath))
            {
                Thread.Sleep(500);
                Extract(finalResourcePath, outputDir);
                Log($".tangoresource file extraction completed!");
                canConvert = true;
            }
            else
            {
                Log($"WARNING: Unable to find a matching.tangoresource file.");
                Log($"WAV conversion will not be possible without the .tangoresource file.");
            }

            var msadpcms = Directory.GetFiles(outputDir, "*.msadpcm", SearchOption.AllDirectories);
            var bsnds = Directory.GetFiles(outputDir, "*.bsnd", SearchOption.AllDirectories);

            if (msadpcms.Length > 0 && bsnds.Length > 0)
            {
                int convertedCount = 0;
                foreach (var msadpcmPath in msadpcms)
                {
                    string justName = Path.GetFileNameWithoutExtension(msadpcmPath);
                    foreach (var bsndPath in bsnds)
                    {
                        if (Path.GetFileNameWithoutExtension(bsndPath)
                              .Equals(justName, StringComparison.OrdinalIgnoreCase))
                        {
                            string wavOut = Path.Combine(
                                Path.GetDirectoryName(msadpcmPath) ?? outputDir,
                                justName + ".wav"
                            );
                            Log($"Creating: {justName}.wav");
                            GenerateWave(msadpcmPath, bsndPath, wavOut);
                            convertedCount++;
                            break;
                        }
                    }
                }

                Log($"WAV generation completed! {convertedCount} WAV files created.");
            }
            else
            {
                Log("No .msadpcm and .bsnd file pair found to generate WAVs.");
            }

            Log("Process completed successfully!");
            if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir) && canConvert)
            {
                Log("Starting automatic WAV conversion after extraction...");
                ConvertExistingFiles(outputDir);

                // After generating the WAVs, we convert them with vgmstream-cli.exe
                string wavOutputFolder = Path.Combine(outputDir, "WAVs");
                if (Directory.Exists(wavOutputFolder))
                {
                    ConvertWavsWithVgmstream(wavOutputFolder);
                }
            }
        }
        public void Extract(string inputPath, string outputPath)
        {
            try
            {
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                using (var file = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(file))
                {
                    long fileLength = file.Length;

                    CheckAvailable(file, 4);
                    byte[] signature = reader.ReadBytes(4);
                    if (signature.Length < 4 || signature[1] != 0x94 || signature[2] != 0xAB || signature[3] != 0xCD)
                    {
                        throw new Exception($"Invalid signature: {BitConverter.ToString(signature)}");
                    }

                    bool isTangoResource = Path.GetExtension(inputPath).ToLower() == ".tangoresource";
                    if (isTangoResource)
                    {
                        CheckAvailable(file, 4);
                        uint headerFilesCount = ReadBigEndianUInt32(reader);
                        Log($"Cabeçalho contém {headerFilesCount} entradas.");

                        for (int i = 0; i < headerFilesCount; i++)
                        {
                            CheckAvailable(file, 4);
                            uint nameSize1 = ReadNameSize(reader);
                            CheckAvailable(file, nameSize1);
                            reader.ReadBytes((int)nameSize1);

                            CheckAvailable(file, 4);
                            uint nameSize2 = ReadNameSize(reader);
                            CheckAvailable(file, nameSize2);
                            reader.ReadBytes((int)nameSize2);

                            CheckAvailable(file, 4);
                            reader.ReadInt32(); // DUMMY
                        }
                    }

                    CheckAvailable(file, 4);
                    uint offset = ReadBigEndianUInt32(reader);
                    if (offset >= fileLength)
                        throw new EndOfStreamException($"Offset ({offset}) maior que o tamanho do arquivo ({fileLength}).");

                    file.Seek(offset, SeekOrigin.Begin);
                    CheckAvailable(file, 12);
                    uint filesCount = ReadBigEndianUInt32(reader);
                    reader.ReadInt32(); // DUMMY
                    reader.ReadInt32(); // DUMMY

                    CheckAvailable(file, 8);
                    uint num1 = ReadBigEndianUInt32(reader);
                    uint num2 = ReadBigEndianUInt32(reader);
                    long skipOffset = file.Position + ((num1 + num2) * 2);
                    if (skipOffset > fileLength)
                        throw new EndOfStreamException("Área de nomes fora do arquivo.");

                    file.Seek(skipOffset, SeekOrigin.Begin);

                    // Read metadata
                    CheckAvailable(file, 8);
                    uint namesSize = ReadBigEndianUInt32(reader);
                    uint namesCount = ReadBigEndianUInt32(reader);
                    Log($"Found {namesCount} names.");

                    long nameBlockStart = file.Position;
                    var names = new List<string>();
                    for (int i = 0; i < namesCount; i++)
                    {
                        if (file.Position >= fileLength)
                            throw new EndOfStreamException("End of file");

                        var nameBytes = new List<byte>();
                        while (true)
                        {
                            byte b = reader.ReadByte();
                            if (b == 0) break;
                            nameBytes.Add(b);
                            if (file.Position >= fileLength)
                                throw new EndOfStreamException("Incomplete name at end of file");
                        }
                        names.Add(Encoding.UTF8.GetString(nameBytes.ToArray()));
                    }

                    long namesEnd = nameBlockStart + namesSize;
                    if (namesEnd > fileLength)
                        throw new EndOfStreamException("Name block exceeds file size.");
                    file.Seek(namesEnd, SeekOrigin.Begin);

                    // Inicia extração dos arquivos
                    SetMaxProgress((int)filesCount);
                    int extractedFiles = 0;

                    for (int i = 0; i < filesCount; i++)
                    {
                        CheckAvailable(file, 16);
                        uint nameIdx = ReadBigEndianUInt32(reader);
                        uint fileOffset = ReadBigEndianUInt32(reader);
                        uint zsize = ReadBigEndianUInt32(reader);
                        uint size = ReadBigEndianUInt32(reader);

                        if (nameIdx < names.Count)
                        {
                            string outName = names[(int)nameIdx];
                            string finalPath = Path.Combine(outputPath, outName);
                            Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? outputPath);

                            Log($"Now extracting -> {outName} (zsize={zsize}, size={size})");
                            long savedPos = file.Position;

                            if (fileOffset + zsize > fileLength)
                            {
                                Log($"Invalid offset for {outName}");
                            }
                            else
                            {
                                file.Seek(fileOffset, SeekOrigin.Begin);
                                byte[] data = reader.ReadBytes((int)zsize);
                                if (zsize == size)
                                {
                                    File.WriteAllBytes(finalPath, data);
                                }
                                else
                                {
                                    //try raw deflate
                                    try
                                    {
                                        using (var mem = new MemoryStream(data))
                                        using (var deflate = new System.IO.Compression.DeflateStream(
                                            mem, System.IO.Compression.CompressionMode.Decompress))
                                        using (var outMem = new MemoryStream())
                                        {
                                            deflate.CopyTo(outMem);
                                            File.WriteAllBytes(finalPath, outMem.ToArray());
                                        }
                                    }
                                    catch
                                    {
                                        //try zlib
                                        try
                                        {
                                            byte[] inflated = ZlibDecompress(data);
                                            File.WriteAllBytes(finalPath, inflated);
                                        }
                                        catch (Exception ex2)
                                        {
                                            Log($"Falha ao descomprimir {outName}: {ex2.Message}");
                                            File.WriteAllBytes(finalPath + ".compressed", data);
                                        }
                                    }
                                }
                                extractedFiles++;
                            }

                            file.Seek(savedPos, SeekOrigin.Begin);
                        }

                        UpdateProgress(i + 1);
                    }
                    //Auto convert (.bsnd + .msadpcm) only after extraction of the 2 files is done
                    if (!string.IsNullOrEmpty(outputPath) && Directory.Exists(outputPath) && canConvert)
                    {
                        Log("Starting automatic WAV conversion post extraction...");
                        ConvertExistingFiles(outputPath);
                    }
                }
            }
            catch (EndOfStreamException eofEx)
            {
                Log($"Failure to read data {eofEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Log($"Error during extraction {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Concatenates .msadpcm + .bsnd into a .wav under a simple test header.
        /// </summary>
        public void GenerateWave(string msadpcmFile, string bsndFile, string wavOut)
        {
            try
            {
                using (var bsndReader = new BinaryReader(File.OpenRead(bsndFile)))
                {
                    // verify bsnf
                    var signature = new string(bsndReader.ReadChars(4));
                    if (signature != "bsnf")
                    {
                        throw new Exception("bsnf signature not found");
                    }
                    bsndReader.BaseStream.Seek(-50, SeekOrigin.End);

                    //audio metadata
                    ushort codecId = bsndReader.ReadUInt16();     // must be 2 for adpcm
                    ushort channels = bsndReader.ReadUInt16();    // channel numbers
                    uint frequency = bsndReader.ReadUInt32();     // sample rate
                    uint bps = bsndReader.ReadUInt32();          // bps
                    ushort align = bsndReader.ReadUInt16();      // block alignment

                    // read 9 unknown values
                    uint[] unknownValues = new uint[9];
                    for (int i = 0; i < 9; i++)
                    {
                        unknownValues[i] = bsndReader.ReadUInt32();
                    }

                    // get msadpcm size
                    long msadpcmSize = new FileInfo(msadpcmFile).Length;

                    // create the output WAV file
                    using (var wavWriter = new BinaryWriter(File.Create(wavOut)))
                    {
                        // RIFF Header
                        uint riffSize = (uint)(msadpcmSize + 70);
                        wavWriter.Write(Encoding.ASCII.GetBytes("RIFF"));
                        wavWriter.Write(riffSize);
                        wavWriter.Write(Encoding.ASCII.GetBytes("WAVE"));

                        // Format Chunk
                        uint fmtSize = 50;
                        wavWriter.Write(Encoding.ASCII.GetBytes("fmt "));
                        wavWriter.Write(fmtSize);
                        wavWriter.Write(codecId);
                        wavWriter.Write(channels);
                        wavWriter.Write(frequency);
                        wavWriter.Write(bps);
                        wavWriter.Write(align);
                        foreach (uint val in unknownValues)
                        {
                            wavWriter.Write(val);
                        }
                        wavWriter.Write(Encoding.ASCII.GetBytes("data"));
                        wavWriter.Write((uint)msadpcmSize);

                        // copy msadpcm data
                        using (var msadpcmReader = new BinaryReader(File.OpenRead(msadpcmFile)))
                        {
                            const int bufferSize = 1024 * 1024; // 1MB buffer
                            byte[] buffer = new byte[bufferSize];
                            int bytesRead;

                            while ((bytesRead = msadpcmReader.Read(buffer, 0, bufferSize)) > 0)
                            {
                                wavWriter.Write(buffer, 0, bytesRead);
                            }
                        }
                    }

                    Log($"Wav file generated {wavOut}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error generating wav {ex.Message}");
                throw;
            }
        }

        private void CheckAvailable(FileStream file, long bytesNeeded)
        {
            if (file.Position + bytesNeeded > file.Length)
                throw new EndOfStreamException("Insufficient data");
        }

        private uint ReadBigEndianUInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length < 4)
                throw new EndOfStreamException("Not enough bytes for UInt32.");
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private uint ReadNameSize(BinaryReader reader)
        {
            uint val = ReadBigEndianUInt32(reader);
            return ReverseUInt32(val);
        }

        private uint ReverseUInt32(uint value)
        {
            return ((value & 0x000000FFu) << 24)
                 | ((value & 0x0000FF00u) << 8)
                 | ((value & 0x00FF0000u) >> 8)
                 | ((value & 0xFF000000u) >> 24);
        }

        private byte[] ZlibDecompress(byte[] data)
        {
            if (data.Length < 2)
                throw new Exception("Insufficient data for zlib");

            using (var mem = new MemoryStream(data))
            {
                int cmf = mem.ReadByte();
                int flg = mem.ReadByte();
                if (cmf == -1 || flg == -1)
                    throw new Exception("Wrong zlib header");

                byte[] rawDeflate = new byte[data.Length - 2];
                Array.Copy(data, 2, rawDeflate, 0, rawDeflate.Length);

                using (var rawMem = new MemoryStream(rawDeflate))
                using (var deflStream = new System.IO.Compression.DeflateStream(
                    rawMem, System.IO.Compression.CompressionMode.Decompress))
                using (var outMem = new MemoryStream())
                {
                    deflStream.CopyTo(outMem);
                    return outMem.ToArray();
                }
            }
        }

        private void Log(string message) => _logCallback?.Invoke(message);
        private void UpdateProgress(int value) => _updateProgressCallback?.Invoke(value);
        private void SetMaxProgress(int value) => _setMaxProgressCallback?.Invoke(value);
        public void ConvertExistingFiles(string searchPath)
        {
            try
            {
                Log($"Trying to search audio files {searchPath}");

                string wavOutputFolder = Path.Combine(searchPath, "WAVs");
                Directory.CreateDirectory(wavOutputFolder);

                string compressedPath = Path.Combine(searchPath, "compressed");
                string generatedPath = Path.Combine(searchPath, "generated");

                var msadpcms = Directory.GetFiles(compressedPath, "*.msadpcm", SearchOption.AllDirectories);
                var bsnds = Directory.GetFiles(generatedPath, "*.bsnd", SearchOption.AllDirectories);
                if (msadpcms.Length == 0 || bsnds.Length == 0)
                {
                    Log("No file pair found");
                    return;
                }
                var bsndDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bsndPath in bsnds)
                {
                    string rel = bsndPath.Substring(generatedPath.Length + 1)
                        .Replace('\\', '/');
                    bsndDict[rel] = bsndPath;
                }
                int convertedCount = 0;
                SetMaxProgress(msadpcms.Length);

                foreach (var msadpcmPath in msadpcms)
                {
                    string rel = msadpcmPath.Substring(compressedPath.Length + 1)
                        .Replace('\\', '/');
                    string dir = Path.GetDirectoryName(rel).Replace('\\', '/');
                    string baseName = Path.GetFileNameWithoutExtension(rel);
                    string bsndRel = Path.Combine(dir, baseName + "_msadpcm.bsnd").Replace('\\', '/');

                    if (bsndDict.TryGetValue(bsndRel, out string bsndPath))
                    {
                        string outputDir = Path.Combine(wavOutputFolder, dir);
                        Directory.CreateDirectory(outputDir);

                        string wavOut = Path.Combine(outputDir, baseName + ".wav");

                        try
                        {
                            Log($"Converting {rel}");
                            GenerateWave(msadpcmPath, bsndPath, wavOut);
                            convertedCount++;
                        }
                        catch (Exception ex)
                        {
                            Log($"Error converting {rel}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Log($"BSND file not found for {rel}");
                    }

                    UpdateProgress(convertedCount);
                }

                Log($"Conversion completed! {convertedCount} WAV files generated in {wavOutputFolder}");
            }
            catch (Exception ex)
            {
                Log($"Error during conversion {ex.Message}");
                throw;
            }
        }

        public string GetRelativePath(string basePath, string fullPath)
        {
            try
            {
                if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    basePath += Path.DirectorySeparatorChar;

                Uri baseUri = new Uri(basePath);
                Uri fullUri = new Uri(fullPath + Path.DirectorySeparatorChar);

                Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
                string relativePath = Uri.UnescapeDataString(relativeUri.ToString()).TrimEnd(Path.DirectorySeparatorChar);

                return relativePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void ConvertWavsWithVgmstream(string wavsFolder)
        {
            try
            {
                string vgmstreamExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vgmstream-cli.exe");
                Log(vgmstreamExe);
                if (!File.Exists(vgmstreamExe))
                {
                    Log("vgmstream not found.");
                    return;
                }

                string outputFolder = Path.Combine(wavsFolder, "vgmstream_converted");
                var wavFiles = Directory.GetFiles(wavsFolder, "*.wav", SearchOption.AllDirectories);
                Log($"Now reconverting {wavFiles.Length} files with vgmstream");

                int count = 0;
                foreach (var wavFile in wavFiles)
                {
                    string dir = Path.GetDirectoryName(wavFile);
                    string fileName = Path.GetFileName(wavFile);
                    string tempOut = Path.Combine(dir, Path.GetFileNameWithoutExtension(wavFile) + "_vgmstream.wav");

                    Log($"Converting {wavFile} to {tempOut}");

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = vgmstreamExe,
                        Arguments = $"-o \"{tempOut}\" \"{wavFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var proc = System.Diagnostics.Process.Start(psi))
                    {
                        string stdOut = proc.StandardOutput.ReadToEnd();
                        string stdErr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();

                        if (proc.ExitCode == 0 && File.Exists(tempOut))
                        {
                            try
                            {
                                File.Delete(wavFile);
                                File.Move(tempOut, wavFile);
                                count++;
                                Log($"Converted and replaced {fileName}");
                            }
                            catch (Exception ex)
                            {
                                Log($"Error replacing {fileName}: {ex.Message}");
                            }
                        }
                        else
                        {
                            Log($"Failed to convert {fileName}: {stdErr}");
                            if (File.Exists(tempOut))
                                File.Delete(tempOut);
                        }
                    }
                }

                Log($"vgmstream converted {count} files in {outputFolder}");
            }
            catch (Exception ex)
            {
                Log($"Error at conversion with vgmstream {ex.Message}");
            }
        }
    }
}
