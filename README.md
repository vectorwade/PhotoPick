# PhotoPick ⚡

> **Ferramenta desktop nativa de triagem (culling) ultrarrápida de fotos RAW com integração direta ao Adobe Lightroom Classic.**

Construído sob medida para fotógrafos e fotógrafas que precisam revisar milhares de fotos RAW em segundos sem esperar o Lightroom renderizar e indexar catálogos pesados.

---

## 🚀 Principais Recursos

- **Extração Ultrarrápida de Previews (< 1ms por foto):** Lê diretamente o JPEG embutido nos arquivos RAW sem demosaicização pesada do sensor.
- **Suporte Multiformato:** Compatível com Canon (`.CR2`, `.CR3`), Sony (`.ARW`), Nikon (`.NEF`), Adobe (`.DNG`), Fujifilm (`.RAF`), Olympus (`.ORF`), Panasonic (`.RW2`) e `.JPG`.
- **Integração 100% Nativa com o Lightroom Classic:** Grava classificações por estrelas (`xmp:Rating`) e rótulos de cor (`xmp:Label`) em arquivos sidecar `.xmp` de forma não-destrutiva, preservando metadados existentes.
- **Atalhos de Teclado Profissionais:**
  - `P`: Marcar como **Pick** (1 Estrela + Rótulo Verde)
  - `X`: Marcar como **Reject** (Rótulo Vermelho)
  - `U`: Limpar marcações (**Unflag**)
  - `1` a `5`: Classificar com 1 a 5 estrelas
  - `0`: Limpar estrelas
  - `6` a `9`: Rótulos de cor (Vermelho, Amarelo, Verde, Azul)
  - `Setas` ou `A`/`D`: Navegar entre fotos
  - `Espaço` ou `Enter`: Alternar entre Grade e Foto Única (Loupe View)
  - `Ctrl + O`: Abrir pasta
  - `Ctrl + S`: Sincronizar/Gravar arquivos `.xmp`
- **Modo Grade & Modo Loupe:** Visualização em mosaico virtualizado ou foto única em alta resolução com pré-carregamento assíncrono.
- **Cache Local Inteligente:** SQLite + cache de thumbnails em disco para reaberturas instantâneas de pastas já visitadas.

---

## 🛠️ Arquitetura do Projeto

Construído em **.NET 10 (C# 13)** com separação modular:

- **`PhotoPick.Core`:** Motor de extração de RAW, serviço atômico de XMP, SQLite e orquestração de sessão.
- **`PhotoPick.Desktop`:** Interface gráfica Windows nativa (WPF) com aceleração Direct3D por hardware e tema escuro fotográfico.
- **`PhotoPick.Cli`:** Utilitário de linha de comando para benchmarks de extração e gravação de metadados em lote.
- **`PhotoPick.Tests`:** Testes automatizados (xUnit) cobrindo XMP, TIFF IFD parsing e orientação EXIF.

---

## 📦 Como Compilar e Executar

### Pré-requisitos
- [.NET 10 SDK](https://dotnet.microsoft.com/download) ou superior no Windows.

### Executar em Desenvolvimento
```bash
git clone https://github.com/vectorwade/PhotoPick.git
cd PhotoPick
dotnet run --project PhotoPick.Desktop/PhotoPick.Desktop.csproj
```

### Publicar Executável Release
```bash
dotnet publish PhotoPick.Desktop/PhotoPick.Desktop.csproj -c Release -r win-x64 --no-self-contained -o ./publish
```

O executável pronto estará em `./publish/PhotoPick.Desktop.exe`.

---

## 📄 Licença
Distribuído sob licença MIT.
