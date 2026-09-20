with open('PhotoPick.Desktop/MainWindow.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace(
    '<TextBlock Text="Criar Pasta \'_SELECIONADAS_LIGHTROOM\' e Abrir"',
    '<TextBlock Text="Isolar Selecionadas (Via Hard Links)"'
)
text = text.replace(
    '<TextBlock Text="ISOLAR ARQUIVOS"',
    '<TextBlock Text="INSTANTÂNEO & ZERO DISCO"'
)
text = text.replace(
    'Copia exclusivamente as fotos escolhidas',
    'Cria um espelho instantâneo das fotos escolhidas'
)

with open('PhotoPick.Desktop/MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(text)
