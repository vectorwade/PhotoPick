# Glossário do PhotoPick & Triagem (Culling)

Este documento estabelece o vocabulário padrão utilizado pelo time interno de fotógrafas e no desenvolvimento do PhotoPick.

---

### 1. Termos de Triagem (Culling)

* **Culling (Triagem / Seleção):** Processo de revisar uma grande quantidade de fotos recém-saídas da câmera para descartar erros (fotos fora de foco, piscadas, testes de luz) e selecionar as melhores ("keepers") antes da edição.
* **Pick (Selecionada):** Foto aprovada para entrar na etapa de revelação/edição no Lightroom. No Photo Mechanic e no PhotoPick, marcada com a tecla `P`.
* **Reject (Rejeitada):** Foto reprovada (descarte). Marcada com a tecla `X`.
* **Unflagged (Neutro / Não revisada):** Foto que ainda não recebeu avaliação ou teve a marcação desfeita. Marcada com `U` ou `0`.
* **Rating (Estrelas):** Classificação de 1 a 5 estrelas (`xmp:Rating`). 
  * `0`: Sem classificação.
  * `1` a `5`: Níveis de avaliação. No fluxo rápido de triagem, a 1ª estrela é normalmente usada como sinônimo de "Pick".
* **Color Label (Rótulo de Cor):** Etiqueta de cor textual (`xmp:Label`) associada à foto. No padrão do Lightroom em inglês: `"Red"`, `"Yellow"`, `"Green"`, `"Blue"`, `"Purple"`.
* **Contact Sheet (Folha de Contato / Grade):** Visualização em mosaico/grade de múltiplos thumbnails de fotos ao mesmo tempo.
* **Loupe View (Visualização Única / Lupa):** Visualização de uma foto individual preenchendo a janela ou tela inteira para análise detalhada de expressão e enquadramento.

---

### 2. Termos Técnicos de Imagem e Arquivos

* **Arquivo RAW:** Arquivo bruto com os dados capturados diretamente pelo sensor da câmera sem compressão destrutiva ou pós-processamento de fábrica (.CR2, .CR3, .ARW, .NEF, etc.).
* **Embedded Preview (Preview Embutido):** Imagem JPEG gerada pela própria câmera no instante do clique e gravada dentro do cabeçalho do arquivo RAW. É utilizada pela câmera para exibir a foto no visor LCD.
* **Demosaicing (Demosaicização):** O processo computacionalmente pesado de converter os dados brutos de um sensor Bayer em uma imagem RGB visível. O Lightroom faz isso; o PhotoPick **não faz**, lendo apenas o preview embutido para atingir velocidade instantânea.
* **Sidecar XMP:** Arquivo de texto XML externo com extensão `.xmp` gravado na mesma pasta e com o mesmo nome do arquivo original (ex.: `IMG_0001.CR3` -> `IMG_0001.xmp`). Usado por softwares profissionais para salvar metadados e edições sem alterar nenhum byte do arquivo RAW original.
* **EXIF Orientation:** Tag de metadado (valores de 1 a 8) que informa a orientação da câmera no momento do disparo (normal, 90° horário, 180°, 270° anti-horário). O PhotoPick interpreta essa tag para rotacionar o preview automaticamente.
