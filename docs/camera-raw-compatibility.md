# Matriz de Compatibilidade de Formatos RAW

Este documento lista os formatos de imagem suportados pelo PhotoPick e a estratégia de extração de preview embutido para cada um.

---

### 1. Formatos Suportados

| Formato | Extensão | Fabricante Principal | Estrutura Interna | Estratégia de Extração do PhotoPick |
|---|---|---|---|---|
| **Canon RAW v2** | `.CR2` | Canon | Baseado em TIFF (Big/Little Endian) | Leitura direta do SubIFD1 (Tag `0x0201` / `0x0202`) |
| **Canon RAW v3** | `.CR3` | Canon | ISO BMFF / QuickTime container | Leitura direta dos átomos `moov`/`uuid`/`PRVW` |
| **Sony ARW** | `.ARW` | Sony | Baseado em TIFF | Leitura do SubIFD (Tag `0x0201` / `0x0202`) |
| **Nikon NEF** | `.NEF` | Nikon | Baseado em TIFF | Leitura do SubIFD ou StripOffsets JPEG |
| **Adobe DNG** | `.DNG` | Universal / Leica / Pentax | Baseado em TIFF | Leitura do SubIFD Preview (Tag `0x0201`) |
| **Fujifilm RAF** | `.RAF` | Fujifilm | Cabeçalho FUJIFILM + TIFF embutido | Localização do bloco TIFF e extração do preview |
| **JPEG / JPG** | `.JPG`, `.JPEG` | Universal | JPEG JFIF / EXIF | Leitura direta do próprio JPEG |

---

### 2. Desempenho Alvo

* **Tempo de leitura por arquivo:** Menos de 5 ms em SSD NVMe / SATA.
* **Consumo de memória:** Menos de 200 MB de RAM para navegar por pastas com 5.000+ fotos, através de virtualização de interface e liberação imediata de streams não utilizados.
