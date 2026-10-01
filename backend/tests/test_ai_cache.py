"""Cache da IA: mesmo contexto canonico = uma chamada so'. Banco falso em
memoria; nenhum teste toca banco."""
import pytest

from app import settings
from app.services import ai_cache, assistant


class FakeResponse:
    status_code = 200

    def __init__(self, text="Baixe sombras primeiro."):
        self._text = text

    def json(self):
        return {"choices": [{"message": {"content": f" {self._text} "}}]}


@pytest.fixture
def ia(monkeypatch):
    """Banco do cache em dicionario e OpenAI falsa que conta as chamadas."""
    store: dict[tuple[str, str], str] = {}

    def fetch_one(sql, params):
        kind, key, _ttl = params
        return {"response": store[(kind, key)]} if (kind, key) in store else None

    def execute(sql, params):
        if sql.lstrip().startswith("INSERT"):
            kind, key, response = params
            store[(kind, key)] = response
        return 1

    monkeypatch.setattr(ai_cache.database, "fetch_one", fetch_one)
    monkeypatch.setattr(ai_cache.database, "execute", execute)
    monkeypatch.setattr(settings, "OPENAI_API_KEY", "sk-teste")
    assistant._global.clear()
    sent = []
    monkeypatch.setattr(assistant.httpx, "post", lambda url, **kw: sent.append(kw) or FakeResponse())
    return store, sent


HW_A = {"Windows": "Windows 11 Pro (build 26200)", "CPU": "AMD Ryzen 7 5700X3D 8-Core Processor", "GPU": "NVIDIA GeForce RTX 5060 Ti",
        "RAM": "31,9 GB", "Armazenamento": "C: SSD NVMe", "Tipo": "Desktop", "Placa-mãe": "MSI B550M", "BIOS": "7C95v1H (05/2025)"}
# Mesmo hardware que importa, outro PC: BIOS, build, letra do disco e RAM reservada diferentes.
HW_B = {**HW_A, "Windows": "Windows 11 Home (build 26100)", "BIOS": "F17 (01/2024)", "Armazenamento": "D: SSD NVMe",
        "RAM": "32 GB", "CPU": "AMD Ryzen(TM) 7 5700X3D  8-Core Processor", "Placa-mãe": "ASUS TUF B550"}


def test_mil_pcs_iguais_custam_uma_chamada(ia):
    store, sent = ia
    first = assistant.game_tips("Counter-Strike 2", HW_A, "PC forte", None)
    for _ in range(5):
        assert assistant.game_tips("Counter-Strike 2", HW_B, "PC forte", None) == first
    assert len(sent) == 1 and len(store) == 1
    # O que vai para a IA e' o texto canonico: sem BIOS, build, letra do disco e com RAM redonda.
    user = sent[0]["json"]["messages"][1]["content"]
    assert "BIOS" not in user and "build" not in user and "C: SSD" not in user and "Placa-mãe" not in user
    assert "RAM: 32 GB" in user and "Ryzen 7 5700X3D 8-Core" in user


def test_hardware_ou_jogo_diferente_nunca_reaproveita(ia):
    _, sent = ia
    assistant.game_tips("Counter-Strike 2", HW_A, "PC forte", None)
    assistant.game_tips("Counter-Strike 2", {**HW_A, "GPU": "NVIDIA GeForce RTX 5060"}, "PC forte", None)
    assistant.game_tips("Valorant", HW_A, "PC forte", None)
    assistant.game_tips("Counter-Strike 2", HW_A, "PC intermediário", None)
    assistant.game_tips("Counter-Strike 2", {**HW_A, "Tipo": "Notebook"}, "PC forte", None)
    assert len(sent) == 5


def test_medicao_arredonda_fps_mas_nao_as_quedas(ia):
    _, sent = ia
    m = {"game": "CS2", "avg_fps": 241.7, "low1_fps": 148.2, "avg_gpu": 71.6, "gpu_drops": 17, "drops": 21}
    assistant.game_tips("Counter-Strike 2", HW_A, "PC forte", m)
    assistant.game_tips("Counter-Strike 2", HW_A, "PC forte", {**m, "avg_fps": 239.9, "low1_fps": 151.0, "avg_gpu": 72.4})
    assert len(sent) == 1
    user = sent[0]["json"]["messages"][1]["content"]
    assert "avg_fps=240" in user and "low1_fps=150" in user and "gpu_drops=17" in user
    assistant.game_tips("Counter-Strike 2", HW_A, "PC forte", {**m, "gpu_drops": 3})
    assert len(sent) == 2


def test_placa_mae_conta_so_na_troca_de_peca(ia):
    _, sent = ia
    args = ([], [], "gpu", "Placa de vídeo no limite.")
    assistant.upgrade(HW_A, *args)
    assistant.upgrade({**HW_A, "BIOS": "outra"}, *args)
    assert len(sent) == 1 and "MSI B550M" in sent[0]["json"]["messages"][1]["content"]
    assistant.upgrade({**HW_A, "Placa-mãe": "ASUS TUF B550"}, *args)
    assert len(sent) == 2


def test_mudar_prompt_ou_modelo_invalida(ia, monkeypatch):
    _, sent = ia
    assistant.game_tips("CS2", HW_A, "PC forte", None)
    monkeypatch.setattr(settings, "OPENAI_MODEL", "outro-modelo")
    assistant.game_tips("CS2", HW_A, "PC forte", None)
    monkeypatch.setattr(assistant, "GAME_TIPS", assistant.GAME_TIPS + " Regra nova.")
    assistant.game_tips("CS2", HW_A, "PC forte", None)
    monkeypatch.setattr(ai_cache, "CACHE_VERSION", "2")
    assistant.game_tips("CS2", HW_A, "PC forte", None)
    assert len(sent) == 4


def test_cache_nao_gasta_teto_global(ia, monkeypatch):
    _, sent = ia
    monkeypatch.setattr(assistant, "GLOBAL_PER_HOUR", 1)
    assistant.game_tips("CS2", HW_A, "PC forte", None)
    # Teto esgotado: a mesma pergunta ainda responde, do cache.
    assert assistant.game_tips("CS2", HW_A, "PC forte", None)
    with pytest.raises(assistant.AssistantUnavailable):
        assistant.game_tips("Valorant", HW_A, "PC forte", None)


def test_banco_fora_do_ar_nao_derruba_a_ia(monkeypatch):
    # Sem fake: o conftest faz qualquer conexao falhar.
    monkeypatch.setattr(settings, "OPENAI_API_KEY", "sk-teste")
    assistant._global.clear()
    sent = []
    monkeypatch.setattr(assistant.httpx, "post", lambda url, **kw: sent.append(kw) or FakeResponse())
    assert assistant.game_tips("CS2", HW_A, "PC forte", None) == "Baixe sombras primeiro."
    assert assistant.game_tips("CS2", HW_A, "PC forte", None) == "Baixe sombras primeiro."
    assert len(sent) == 2


def test_erro_da_openai_nao_e_guardado(ia, monkeypatch):
    store, _ = ia

    class Erro:
        status_code = 500

    monkeypatch.setattr(assistant.httpx, "post", lambda url, **kw: Erro())
    with pytest.raises(assistant.AssistantUnavailable):
        assistant.game_tips("CS2", HW_A, "PC forte", None)
    assert store == {}


def test_sem_chave_continua_indisponivel_mesmo_com_cache(ia, monkeypatch):
    assistant.game_tips("CS2", HW_A, "PC forte", None)
    monkeypatch.setattr(settings, "OPENAI_API_KEY", "")
    with pytest.raises(assistant.AssistantUnavailable):
        assistant.game_tips("CS2", HW_A, "PC forte", None)
