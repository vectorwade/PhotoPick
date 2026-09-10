using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoPick.Desktop.Services;

/// <summary>
/// Utilitário nativo do Windows para enviar arquivos com segurança para a Lixeira (Recycle Bin),
/// preservando a integridade dos dados e permitindo recuperação se necessário.
/// </summary>
public static class FileTrashHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.U4)]
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public short fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const int FO_DELETE = 0x0003;
    private const int FOF_ALLOWUNDO = 0x0040;           // Move para a Lixeira do Windows
    private const int FOF_NOCONFIRMATION = 0x0010;      // Não exibe prompt nativo do Windows (já confirmado na UI)
    private const int FOF_SILENT = 0x0004;              // Não exibe diálogo de progresso do Windows Explorer

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    /// <summary>
    /// Envia o arquivo especificado para a Lixeira do Windows.
    /// Se o dispositivo não suportar lixeira (ex: pendrive FAT32/rede), efetua exclusão definitiva como fallback.
    /// </summary>
    public static bool SendToTrash(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

        try
        {
            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0', // Deve terminar com duplo null
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };

            int result = SHFileOperation(ref shf);
            if (result == 0 && !shf.fAnyOperationsAborted)
            {
                return true;
            }
        }
        catch { }

        // Fallback para File.Delete caso a API shell falhe
        try
        {
            File.Delete(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
