#apiMediaPipe.py
from flask import Flask, request, Response
import cv2
import numpy as np
import mediapipe as mp

app = Flask(__name__)

# Inicializa o MediaPipe Hands e configura os parâmetros
mp_hands = mp.solutions.hands
hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=1,
    min_detection_confidence=0.7,
    min_tracking_confidence=0.5
)
mp_draw = mp.solutions.drawing_utils

def detectar_mao(img):
    """
    Detecta a mão usando o MediaPipe Hands e determina:
    - O estado da mão: "Free" (mão aberta) ou "Hold" (mão fechada), 
      baseado na contagem dos dedos estendidos (indicador, médio, anelar, mínimo e polegar).
    - A posição horizontal da mão na imagem, dividida em três partes: Left, Center e Right.
    
    Se nenhum mão for detectada, retorna "Free Center" como valor padrão.
    """
    # Redimensiona a imagem para processamento (320x240)
    img = cv2.resize(img, (320, 240))
    img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    
    # Processa a imagem com MediaPipe Hands
    results = hands.process(img_rgb)
    
    # Se nenhuma mão for detectada, assumir "Free Center"
    if not results.multi_hand_landmarks:
        return "Free Center"
    
    # Considera a primeira mão detectada (máximo 1)
    hand_landmarks = results.multi_hand_landmarks[0]
    landmarks = hand_landmarks.landmark
    extended_fingers = 0

    # Verifica se cada dedo está estendido (eixo y: menor valor indica posição mais alta na imagem)
    if landmarks[8].y < landmarks[6].y:      # Dedo indicador
        extended_fingers += 1
    if landmarks[12].y < landmarks[10].y:    # Dedo médio
        extended_fingers += 1
    if landmarks[16].y < landmarks[14].y:    # Dedo anelar
        extended_fingers += 1
    if landmarks[20].y < landmarks[18].y:    # Dedo mínimo
        extended_fingers += 1
    if landmarks[4].x > landmarks[2].x:      # Polegar (verificação lateral para mão direita)
        extended_fingers += 1

    # Se mais de 1 dedo estiver estendido, consideramos como "Free", caso contrário "Hold"
    state = "Free" if extended_fingers > 1 else "Hold"
    
    # Calcula a posição horizontal da mão com a média dos valores normalizados dos x dos landmarks
    avg_x = sum([lm.x for lm in landmarks]) / len(landmarks)
    if avg_x < 0.33:
        pos = "Left"
    elif avg_x > 0.66:
        pos = "Right"
    else:
        pos = "Center"
    
    # Retorna uma string combinando o estado e a posição, ex: "Free Left" ou "Hold Center"
    return f"{state} {pos}"

# Rota da API que recebe a imagem via POST e retorna o resultado (estado e posição da mão)
@app.route('/', methods=['POST'])
def analisar():
    img_bytes = request.data
    npimg = np.frombuffer(img_bytes, np.uint8)
    img = cv2.imdecode(npimg, cv2.IMREAD_COLOR)
    
    if img is None:
        return Response("Imagem inválida", status=400)
    
    resultado = detectar_mao(img)
    return Response(resultado, mimetype='text/plain')

# Execução da API Flask
if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000)
