<p align="center">
  <img src="PhotoPick.Desktop/app_icon.png" width="120" alt="MaviSelect Logo" />
  <h1 align="center">MaviSelect ⚡</h1>
  <p align="center">
    <strong>Ferramenta desktop de triagem (culling) ultrarrápida de fotos RAW com integração direta ao Adobe Lightroom Classic.</strong>
  </p>
  <p align="center">
    <a href="installer/MaviSelect_Setup.msi"><img src="https://img.shields.io/badge/Download-Instalador%20MSI%20(v1.0)-blue?style=for-the-badge&logo=windows" alt="Download MSI" /></a>
    <img src="https://img.shields.io/badge/.NET-10.0%20WPF-purple?style=for-the-badge" alt=".NET 10" />
    <img src="https://img.shields.io/badge/Lightroom%20Classic-Integrado-blue?style=for-the-badge&logo=adobe" alt="Lightroom Classic" />
  </p>
</p>

---

O **MaviSelect** é uma alternativa moderna, leve e sob medida ao Photo Mechanic, construída para fotógrafos que precisam revisar milhares de fotos RAW em segundos sem esperar o Lightroom renderizar e indexar catálogos pesados.

---

## 🚀 Principais Recursos

### ⚡ Performance & Culling Instantâneo
- **Extração Ultrarrápida (< 1ms por foto):** Lê diretamente o JPEG embutido nos arquivos RAW sem demosaicização pesada do sensor.
- **Virtualização Real de Grade:** Navegue por pastas com mais de 1.000 fotos com fluidez total e consumo de memória inferior a 90MB.
- **Suporte Multiformato Completo:** Canon (`.CR2`, `.CR3`), Sony (`.ARW`), Nikon (`.NEF`), Adobe (`.DNG`), Fujifilm (`.RAF`), Olympus (`.ORF`), Panasonic (`.RW2`) e `.JPG`.

### 🎨 Experiência Visual & Controles
- **Aura Luminosa (Glow):**
  - 🟢 **Verde Neon:** Fotos marcadas como **Escolhidas (Pick)**.
  - 🔴 **Vermelha:** Fotos marcadas como **Rejeitadas (Reject)**.
- **Controles Híbridos (Mouse + Teclado):**
  - Botões físicos dedicados nos cards e no zoom: **✔ Escolher**, **✖ Rejeitar** e **↺ Limpar**.
  - **5 Estrelas Clicáveis:** Clique diretamente na estrela desejada para classificar instantaneamente ou use as teclas numéricas.
- **Filtros Rápidos no Topo:**
  - Filtragem por status: *Todas*, *✔ Selecionadas*, *✖ Rejeitadas*, *○ Não Avaliadas*.
  - Filtragem por notas: botões rápidos de *★ 1* a *★ 5*.

### 📸 Integração com Adobe Lightroom Classic
- **Envio Apenas das Selecionadas:** Opção de criar uma pasta dedicada `_SELECIONADAS_LIGHTROOM` contendo apenas os RAWs escolhidos com seus arquivos `.xmp` e abrir o Lightroom Classic focado exclusivamente nelas.
- **Gravação XMP Atômica e Não-Destrutiva:** Salva notas (`xmp:Rating`) e rótulos (`xmp:Label="Green"`) sem alterar direitos autorais ou metadados de câmera existentes.
- **Exportação Rápida:** Botão para copiar apenas as fotos escolhidas para qualquer pasta ou pendrive em um clique.

---

## ⌨️ Atalhos de Teclado

| Tecla | Ação |
|---|---|
| `P` | Marcar como **Pick** (1 Estrela + Rótulo Verde) e avançar |
| `X` | Marcar como **Reject** (Rótulo Vermelho) e avançar |
| `U` | Limpar marcações (**Unflag**) e avançar |
| `1` a `5` | Classificar com 1 a 5 estrelas |
| `0` | Limpar estrelas |
| `6` a `9` | Rótulos de cor (Vermelho, Amarelo, Verde, Azul) |
| `Setas` ou `A` / `D` | Navegar entre as fotos |
| `Espaço` ou `Enter` | Alternar entre Grade e Foto Única (Modo Loupe) |
| `Ctrl + O` | Abrir pasta de fotos |
| `Ctrl + S` | Salvar e sincronizar metadados `.xmp` |

---

## 📦 Instalação & Execução

### Opção 1: Instalador Oficial do Windows (.MSI)
Baixe e execute o arquivo:
👉 **[`installer/MaviSelect_Setup.msi`](installer/MaviSelect_Setup.msi)**

*O instalador configura o MaviSelect em Arquivos de Programas, adiciona atalhos com o ícone oficial na Área de Trabalho e no Menu Iniciar, e permite atualizações ou desinstalações automáticas.*

### Opção 2: Versão Portátil Direta
O executável compilado em Release está disponível em:
```
publish/MaviSelect.exe
```

---

## 🛠️ Compilação a partir do Código-Fonte

### Pré-requisitos
- [.NET 10 SDK](https://dotnet.microsoft.com/download) ou superior.
- Windows 10/11 x64.

### Compilar e Rodar
```powershell
# Clonar o repositório
git clone https://github.com/vectorwade/PhotoPick.git
cd PhotoPick

# Executar a aplicação
dotnet run --project PhotoPick.Desktop/PhotoPick.Desktop.csproj
```

### Gerar Publicação e Instalador
```powershell
# Publicar Release
dotnet publish PhotoPick.Desktop/PhotoPick.Desktop.csproj -c Release -o ./publish

# Compilar instalador MSI (requer ferramenta wix)
wix build installer/Package.wxs -o installer/MaviSelect_Setup.msi
```

---

## 🏗️ Estrutura da Solução

- **`PhotoPick.Core`:** Motor de extração ultrarrápida de JPEG embutido em RAW, manipulador de XMP atômico, banco SQLite e lógica de sessão.
- **`PhotoPick.Desktop`:** Aplicação desktop nativa Windows (WPF/C# 13) sob o nome executável `MaviSelect.exe`.
- **`installer`:** Definição do pacote WiX Toolset v7 gerando o instalador `MaviSelect_Setup.msi`.
- **`PhotoPick.Tests`:** Testes automatizados (xUnit) cobrindo XMP, parsers de tags IFD/TIFF e rotação EXIF.

---

## 📄 Licença
Distribuído sob licença MIT.
