using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Seagull.Interior_01.Utility;
using UnityEngine;
using UnityEngine.Events;

namespace Seagull.Interior_01 {
    public class SampleScene : MonoBehaviour {
        private List<TurnOnAble> turnOffables;
        public List<TurnOnAble> turnOnable = new();
        public List<Rigidbody> pushable = new();
        private bool hasLoggedNullTurnOn;
        private bool hasLoggedMissingOnTurnOnField;

        private void Start() {
            // Remove referências quebradas para evitar NRE em runtime.
            turnOnable.RemoveAll(item => item == null);
            pushable.RemoveAll(item => item == null);
            turnOffables = new List<TurnOnAble>(turnOnable);
            Invoke(nameof(closeAfter10Seconds), 9f);
        }

        private void OnDisable() {
            CancelInvoke(nameof(closeAfter10Seconds));
            StopAllCoroutines();
        }

        private int updateTime = 0;
        private void FixedUpdate() {
            updateTime++;
            if (updateTime < 50) return;
            if (turnOnable.Count == 0) return;

            foreach (var obj in turnOnable) {
                if (obj == null) continue;
                start(obj);
            }

            turnOnable.Clear();
        }

        private void Update() {
            foreach (var obj in pushable) {
                if (obj == null) continue;
                obj.AddForce(80, 0, 80);
            }
        }

        private void start(MonoBehaviour obj) {
            int blinkTime = UnityEngine.Random.Range(0, 3);
            turnOn(obj);
            float accumulativeTime = 0;
            for (int i = 0; i < blinkTime; i++) {
                float lightTime = UnityEngine.Random.Range(0f, 0.5f);
                accumulativeTime += lightTime;
                StartCoroutine(invokeTurnOffAfterTime(accumulativeTime, obj));
                float blackTime = UnityEngine.Random.Range(0f, 0.5f);
                accumulativeTime += blackTime;
                StartCoroutine(invokeTurnOnAfterTime(accumulativeTime, obj));
            }
        }

        private void closeAfter10Seconds() {
            foreach (var obj in turnOffables)
                if (obj != null) turnOff(obj);
        }
        
        private IEnumerator invokeTurnOffAfterTime(float delay, MonoBehaviour obj) {
            yield return new WaitForSeconds(delay);
            if (obj == null) yield break;
            turnOff(obj); // 调用带参数的方法
        }
        
        private IEnumerator invokeTurnOnAfterTime(float delay, MonoBehaviour obj) {
            yield return new WaitForSeconds(delay);
            if (obj == null) yield break;
            turnOn(obj); // 调用带参数的方法
        }

        private void turnOn(MonoBehaviour obj) {
            if (obj == null) {
                if (!hasLoggedNullTurnOn) {
                    Debug.LogWarning("SampleScene: objeto nulo passado para turnOn (logado uma vez).");
                    hasLoggedNullTurnOn = true;
                }
                return;
            }

            var field = obj.GetType().GetField("onTurnOn", BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.FieldType != typeof(UnityEvent)) {
                if (!hasLoggedMissingOnTurnOnField) {
                    Debug.LogWarning("SampleScene: campo public UnityEvent onTurnOn não encontrado (logado uma vez).");
                    hasLoggedMissingOnTurnOnField = true;
                }
                return;
            }

            UnityEvent turnOnEvent = (UnityEvent)field.GetValue(obj);
            turnOnEvent?.Invoke();
        }


        private void turnOff(MonoBehaviour obj) {
            if (obj == null) return;
            // 查找是否有名为 "onTurnOff" 的 public 变量
            var field1 = obj.GetType().GetField("onTurnOff", BindingFlags.Public | BindingFlags.Instance);

            // 检查该变量是否是 UnityEvent 类型
            if (field1 == null || field1.FieldType != typeof(UnityEvent)) return;
            // 获取 UnityEvent 实例
            UnityEvent turnOffEvent = (UnityEvent)field1.GetValue(obj);

            // 调用 UnityEvent 的 Invoke 方法
            turnOffEvent?.Invoke();
        }
    }
}