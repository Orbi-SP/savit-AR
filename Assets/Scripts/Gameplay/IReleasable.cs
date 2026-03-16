public interface IReleasable
{
    // Chamado quando a API detecta transição de "segurando -> solto"
    void OnRelease();
}
