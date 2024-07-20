//
// Found in a gist by Dedmen Miller <dedmen@dedmen.de> at:
// https://gist.github.com/dedmen/ab740ad9ebfde0403e8223480bef91ae

public class GitPktLine
{
    // Git Pkt-line protocol
    // https://git-scm.com/docs/gitprotocol-common
    // https://github.com/git-lfs/pktline

    //public static FileStream debugLog = new FileStream("p:/log", FileMode.Create);

    private static void WritePacketInt(string data, Stream output)
    {
        // Size
        var packetLength = data.Length + 4 + 1; // + 4byte length, + terminating LF

        output.Write(System.Text.Encoding.ASCII.GetBytes(packetLength.ToString("x4")));
        output.Write(System.Text.Encoding.ASCII.GetBytes(data));
        output.Write(new []{(byte)'\n'}); // Terminating LF //#TODO this is optional.. its probably easier to just omit it. Including it in binary data is error, excluding it in text data is fine

        //{
        //    debugLog.Write(System.Text.Encoding.ASCII.GetBytes(packetLength.ToString("x4")));
        //    debugLog.Write(System.Text.Encoding.ASCII.GetBytes(data));
        //    debugLog.Write(new[] { (byte)'\n' }); // Terminating LF
        //    debugLog.Flush();
        //}
    }

    private static void WritePacketInt(byte[] data, int bufferLength, Stream output)
    {
        // Size
        var packetLength = bufferLength + 4 /*+ 1*/; // + 4byte length, + terminating LF

        output.Write(System.Text.Encoding.ASCII.GetBytes(packetLength.ToString("x4")));
        output.Write(data, 0, bufferLength);
        //output.Write(new[] { (byte)'\n' }); // Terminating LF

        //{
        //    debugLog.Write(System.Text.Encoding.ASCII.GetBytes(packetLength.ToString("x4")));
        //    debugLog.Write(data, 0, bufferLength);
        //    debugLog.Write(new[] { (byte)'\n' }); // Terminating LF
        //    debugLog.Flush();
        //}
    }

    public static void WriteMessage(string message, Stream target)
    {
        using var output = new MemoryStream();
        WritePacketInt(message, output);
        {
            output.Seek(0, SeekOrigin.Begin);
            Console.WriteLine(System.Text.Encoding.ASCII.GetString(output.GetBuffer()));
        }

        output.CopyTo(target);
    }

    public static void WriteMessagePacketList(IEnumerable<string> messages, Stream target)
    {
        using var output = new MemoryStream();
        // List of packets, terminated by a flush

        foreach (var message in messages)
        {
            WritePacketInt(message, output);
        }
        Flush(output);

        output.Seek(0, SeekOrigin.Begin);
        //Console.WriteLine(">" + System.Text.Encoding.ASCII.GetString(output.GetBuffer()));
        output.CopyTo(target);
        target.Flush();
    }

    public static void Flush(Stream target)
    {
        target.Write("0000"u8);
        target.Flush();
    }

    public static void Delim(Stream target)
    {
        target.Write("0001"u8);
    }

    public static byte[] ReadMessage(Stream source, bool allowLF = true)
    {
        var pktLength = new byte[4];
        var numRead = source.Read(pktLength, 0, 4);
        if (numRead == 0) return Array.Empty<byte>();
        //debugLog.Write(pktLength);

        try
        {
            var packetLength = Convert.ToInt32(System.Text.Encoding.ASCII.GetString(pktLength), 16);


            if (packetLength < 4)
            {
                // Special packet, lets assume its a flush
                //Console.WriteLine("<" + System.Text.Encoding.ASCII.GetString(pktLength));
                //if (packetLength != 0)
                //    Debugger.Break();

                //#TODO handle flush and others?
                return Array.Empty<byte>();
            }

            packetLength -= 4; /* deduct the 4byte length itself */

            byte[] pkt;

            if (allowLF)
            {
                // Usually terminated by a LF, lets assume it'll be there and we'll want to skip it

                pkt = new byte[packetLength - 1];
                int offset = 0;
                while (offset < packetLength-1) // Handle if data isn't avail yet, we know how large the packet will be
                {
                    numRead = source.Read(pkt, offset, packetLength-1 - offset);
                    offset += numRead;
                }


                var lastByte = source.ReadByte();

                if (lastByte != '\n')
                {
                    // Oh its not LF terminated, welp that's a bummer, we have to put into a new buffer

                    var newArray = new byte[packetLength];
                    Array.Copy(pkt, 0, newArray, 0, packetLength - 1);
                    newArray[packetLength - 1] = (byte)lastByte;
                    pkt = newArray;
                }

                //debugLog.Write(pkt);

                //Console.WriteLine("<" + System.Text.Encoding.ASCII.GetString(pktLength) + System.Text.Encoding.ASCII.GetString(pkt));
            }
            else
            {
                pkt = new byte[packetLength];
                int offset = 0;
                while (offset < packetLength) // Handle if data isn't avail yet, we know how large the packet will be
                {
                    numRead = source.Read(pkt, offset, packetLength - offset);
                    offset += numRead;
                }

                //debugLog.Write(pkt);

                //Console.WriteLine("<" + System.Text.Encoding.ASCII.GetString(pktLength) + " <binary>"); // allowLF is only false for expected binary data
            }

            return pkt;
        }
        catch (System.FormatException ex)
        {
            //debugLog.Flush(true);
            //var scratch = new byte[8192];
            //numRead = source.Read(scratch, 0, 8192);
            //Debugger.Break();
        }

        return null;
    }

    public static IEnumerable<string> ReadMessagePacketList(Stream source)
    {
        var result = new List<string>();
        while (true)
        {
            var msg = ReadMessage(source, true);

            if (msg.Length == 0) // flush
                break;

            result.Add(Encoding.ASCII.GetString(msg));
        }
        return result;
    }

    public static IEnumerable<byte[]> ReadMessagePacketListBinary(Stream source)
    {
        var result = new List<byte[]>();
        while (true)
        {
            var msg = ReadMessage(source, false);

            if (msg.Length == 0) // flush
                break;

            result.Add(msg);
        }
        return result;
    }

    //! Write all data from input stream out as packets
    public static void WriteStreamData(Stream input, Stream target)
    {
        // All data in 8192 chunks (65kb is max, so we could be bigger), terminated by a flush
        var buffer = new byte[8192];
        var sentLength = 0;
        do
        {
            sentLength = input.Read(buffer);

            if (sentLength > 0)
                WritePacketInt(buffer, sentLength, target);

            // Console.WriteLine($"S> {Encoding.ASCII.GetString(buffer)}");

        } while (sentLength == buffer.Length);

        target.Flush();
    }

    // Read multiple packets from input stream, and send all the data to target
    public static void ReadStreamData(Stream input, Stream target)
    {
        var buffers = ReadMessagePacketListBinary(input); //#TODO this will load the complete data into memory. We could instead just fetch chunks and write target per chunk, lowers memory usage

        foreach (var bytes in buffers)
        {
            target.Write(bytes);
        }
    }
}

public class LFSFilter : Filter
{
    private Process processFilterP;
    private bool errorFlag = false;

    public LFSFilter() : base("lfs", new[] { new FilterAttributeEntry("lfs") })
    {
        // We can start one filter process, and keep using it. Instead of starting/stopping for each file
    }

    protected override void Clean(string path, string root, Stream input, Stream output)
    {
        //Console.WriteLine($"LFS Clean {path}");
        // The input buffer is only 65536 bytes large, this function will get called repeatedly for the same path, until all data is passed through

        // Run

        // https://github.com/git-lfs/git-lfs/blob/main/commands/command_filter_process.go#L83
        // Payload end is identified by sending a Flush

        // payload data
        GitPktLine.WriteStreamData(input, processFilterP.StandardInput.BaseStream);

        // After we've sent all data, we'll go to Complete, send a Flush to signify end, and read the results
    }

    protected override void Complete(string path, string root, Stream output)
    {
        // Communicate that we are done transmitting this file
        GitPktLine.Flush(processFilterP.StandardInput.BaseStream);
        // Now we can read outputs
        GitPktLine.ReadStreamData(processFilterP.StandardOutput.BaseStream, output);



        var status2 = GitPktLine.ReadMessagePacketList(processFilterP.StandardOutput.BaseStream); // status=success (Execution has finished)

        //Console.WriteLine($"LFS Complete {path}");

        output.Flush();
        output.Close();

        if (errorFlag || status2.First() != "status=success")
        {
            throw new Exception($"LFS returned errors {status2.First()}");
        }
    }

    protected override void Create(string path, string root, FilterMode mode)
    {
        Console.WriteLine($"LFS Create {path} {mode}");

        //GitPktLine.debugLog.Dispose();
        //GitPktLine.debugLog = new FileStream($"p:/log{Path.GetFileName(path)}", FileMode.Create);

        if (processFilterP == null)
        {
            try
            {
                // launch git-lfs
                processFilterP = new Process();
                processFilterP.StartInfo.FileName = "git-lfs";
                processFilterP.StartInfo.Arguments = "filter-process";
                processFilterP.StartInfo.WorkingDirectory = root;
                processFilterP.StartInfo.RedirectStandardInput = true;
                processFilterP.StartInfo.RedirectStandardOutput = true;
                processFilterP.StartInfo.RedirectStandardError = true;
                processFilterP.StartInfo.CreateNoWindow = true;
                processFilterP.StartInfo.UseShellExecute = false;

                processFilterP.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        Console.WriteLine($"LFS F E: {args.Data}");
                        errorFlag = true;
                    }

                };

                processFilterP.EnableRaisingEvents = true;

                processFilterP.Start();

                processFilterP.BeginErrorReadLine();


                // Init // https://git-scm.com/docs/long-running-process-protocol

                GitPktLine.WriteMessagePacketList(new[] { "git-filter-client", "version=2" }, processFilterP.StandardInput.BaseStream);
                var serverInit = GitPktLine.ReadMessagePacketList(processFilterP.StandardOutput.BaseStream);


                // capabilities
                GitPktLine.WriteMessagePacketList(new []{ "capability=clean", "capability=smudge" }, processFilterP.StandardInput.BaseStream);
                var supportedCaps = GitPktLine.ReadMessagePacketList(processFilterP.StandardOutput.BaseStream);

                // ready for commands now
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        GitPktLine.WriteMessagePacketList(new[] { mode == FilterMode.Clean ? "command=clean" : "command=smudge", $"pathname={path}" }, processFilterP.StandardInput.BaseStream);
        var status = GitPktLine.ReadMessagePacketList(processFilterP.StandardOutput.BaseStream); // status=success (command was accepted)
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void Smudge(string path, string root, Stream input, Stream output)
    {
        // Run
        // The input buffer is only 65536 bytes large, this function will get called repeatedly for the same path, until all data is passed through

        // https://github.com/git-lfs/git-lfs/blob/main/commands/command_filter_process.go#L93

        // payload data
        GitPktLine.WriteStreamData(input, processFilterP.StandardInput.BaseStream);

        // After we've sent all data, we'll go to Complete, send a Flush to signify end, and read the results
    }

    private static Process RunLFSProcess(string root, string command)
    {
        // launch git-lfs
        var process = new Process();
        process.StartInfo.FileName = "git-lfs";
        process.StartInfo.Arguments = command;
        process.StartInfo.WorkingDirectory = root;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = false;
        process.StartInfo.UseShellExecute = false;

        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                Console.WriteLine($"LFS E: {args.Data}");
        };
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                Console.WriteLine($"LFS O: {args.Data}");
        };

        process.EnableRaisingEvents = true;

        process.Start();

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return process;
    }


    // https://git-scm.com/docs/githooks

    public static void PrePush(string root, IEnumerable<PushUpdate> updates)
    {
        var process = RunLFSProcess(root, $"pre-push origin");

        foreach (var update in updates)
        {
            process.StandardInput.Write($" {update.DestinationRefName} {update.DestinationObjectId} {update.SourceRefName} {update.SourceObjectId}\n");
        }
        process.StandardInput.Flush();
        process.StandardInput.Close();

        process.WaitForExit();
    }

    public static void PostCheckout(string root, string oldRef, string newRef)
    {
        var process = RunLFSProcess(root, $"post-checkout {oldRef} {newRef} 0");
        process.WaitForExit();
    }

    public static void PostCommit(string root)
    {
        var process = RunLFSProcess(root, "post-commit");
        process.WaitForExit();
    }
}