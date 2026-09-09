# Especificação de Metadados XMP x Lightroom Classic

Este documento detalha o comportamento do Adobe Lightroom Classic ao ler e gravar metadados XMP, e como o PhotoPick garante 100% de compatibilidade sem conflitos.

---

### 1. Estrutura Padrão do Sidecar XMP

O Lightroom Classic espera arquivos `.xmp` gravados em formato UTF-8 (sem BOM ou com BOM padrão), seguindo o schema RDF da Adobe:

```xml
<?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="Adobe XMP Core 5.6-c140">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:xmp="http://ns.adobe.com/xap/1.0/"
    xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/">
   <xmp:Rating>5</xmp:Rating>
   <xmp:Label>Green</xmp:Label>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
```

---

### 2. Mapeamento de Atributos

| Campo no PhotoPick | Tag XMP | Tipo | Valores Válidos | Reconhecido pelo Lightroom? |
|---|---|---|---|---|
| **Classificação por Estrelas** | `<xmp:Rating>` | Inteiro | `0` a `5` | **SIM (100%)** |
| **Rótulo de Cor** | `<xmp:Label>` | Texto | `"Red"`, `"Yellow"`, `"Green"`, `"Blue"`, `"Purple"` | **SIM (100% no padrão inglês)** |
| **Pick / Reject Flag** | N/A | N/A | Exclusivo do banco `.lrcat` | **NÃO** (O Lightroom não lê flags de sidecars) |

> [!NOTE]
> **Estratégia para Pick (P):**
> Quando a fotógrafa pressiona `P`, o PhotoPick atribui `<xmp:Rating>1</xmp:Rating>` (e/ou `<xmp:Label>Green</xmp:Label>`).
> Quando pressiona `X` (Reject), atribui `<xmp:Label>Red</xmp:Label>`.
> Ao abrir o Lightroom Classic, a fotógrafa filtra por "1 estrela" ou "verde" na barra de filtros de biblioteca (`\`), obtendo imediatamente todas as fotos selecionadas na triagem.

---

### 3. Regras de Não-Destrutividade e Preservação

Ao gravar a seleção no PhotoPick:
1. **Se o arquivo `.xmp` já existir:**
   * O PhotoPick analisa a árvore XML existente.
   * Preserva todas as tags já presentes (ex.: `<dc:subject>`, `<crs:*>` edições prévias de revelação, copyright, etc.).
   * Apenas substitui ou insere `<xmp:Rating>` e `<xmp:Label>`.
2. **Se o arquivo `.xmp` não existir:**
   * O PhotoPick cria um arquivo sidecar novo e limpo.
3. **Arquivos originais RAW (.CR2, .CR3, .ARW, etc.):**
   * **NUNCA são modificados**. O PhotoPick abre os arquivos RAW em modo estritamente somente-leitura (`FileAccess.Read`, `FileShare.ReadWrite`).
