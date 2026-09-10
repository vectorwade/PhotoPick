import hmac
import hashlib
import sys

MASTER_SALT = "MAVI_SELECT_SECRET_V1_2026_PRO_KEY"

def generate_key(machine_id: str) -> str:
    clean_id = machine_id.strip().upper()
    h = hmac.new(MASTER_SALT.encode("utf-8"), clean_id.encode("utf-8"), hashlib.sha256)
    hex_str = h.hexdigest().upper()
    return f"MAVI-{hex_str[0:4]}-{hex_str[4:8]}-{hex_str[8:12]}-{hex_str[12:16]}"

def main():
    print("=" * 60)
    print("   GERADOR DE CHAVES DE ATIVAÇÃO — MAVI SELECT")
    print("=" * 60)
    if len(sys.argv) > 1:
        mid = sys.argv[1]
    else:
        mid = input("Digite o ID da Máquina do cliente (ex: 4A8F-9C12-88E1-B09A): ")

    if not mid.strip():
        print("ID da Máquina inválido.")
        return

    key = generate_key(mid)
    print("\n" + "-" * 60)
    print(f"ID da Máquina:    {mid.strip().upper()}")
    print(f"CHAVE DE ATIVAÇÃO: {key}")
    print("-" * 60 + "\n")

if __name__ == "__main__":
    main()
