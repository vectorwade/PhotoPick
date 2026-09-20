import re

with open('PhotoPick.Core/Services/CullingSession.cs', 'r', encoding='utf-8') as f:
    text = f.read()

pinvoke_code = """
    [System.Runtime.InteropServices.DllImport("Kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, nint lpSecurityAttributes);

    private static void CreateHardLinkOrCopy(string sourceFileName, string destFileName)
    {
        try
        {
            if (File.Exists(destFileName))
                File.Delete(destFileName);

            bool success = CreateHardLink(destFileName, sourceFileName, 0);
            if (!success)
            {
                File.Copy(sourceFileName, destFileName, true);
            }
        }
        catch
        {
            File.Copy(sourceFileName, destFileName, true);
        }
    }
"""

class_idx = text.find("public class CullingSession")
brace_idx = text.find("{", class_idx)
text = text[:brace_idx+1] + "\n" + pinvoke_code + text[brace_idx+1:]

text = text.replace("File.Copy(photo.FilePath, destPhoto, overwrite: true);", "CreateHardLinkOrCopy(photo.FilePath, destPhoto);")
text = text.replace("File.Copy(xmpSource, destXmp, overwrite: true);", "CreateHardLinkOrCopy(xmpSource, destXmp);")

with open('PhotoPick.Core/Services/CullingSession.cs', 'w', encoding='utf-8') as f:
    f.write(text)
