// Generado por demo/montar.sh: no editar a mano.
window.DEMO = {
  "completa": "video/demo-completa.mp4",
  "duracion": 169.4,
  "historias": [
    {
      "id": "HU-02",
      "spec": "frontend/e2e/locations.spec.ts",
      "titulo": "Gestionar ubicaciones",
      "rol": "Operario de almacén (y cuenta de solo lectura)",
      "historia": "Como operario, quiero dar de alta, buscar, corregir y retirar ubicaciones para saber dónde está físicamente cada artículo.",
      "previstos": [
        "Alta, búsqueda, revisión y retirada de ubicaciones (recorrido habitual)",
        "Código duplicado o datos no válidos (error)",
        "Cuenta de solo lectura sin gestión (permisos)",
        "Retirada bloqueada por existencias, reservas o conteo abierto (estado bloqueante)",
        "Respuesta perdida recuperada sin duplicar (fiabilidad)"
      ],
      "escenarios": [
        {
          "titulo": "un operario da de alta, busca, revisa y retira ubicaciones con el teclado",
          "t": 4
        },
        {
          "titulo": "una cuenta de solo lectura consulta ubicaciones pero no puede gestionarlas",
          "t": 31.7
        },
        {
          "titulo": "un alta con respuesta perdida se recupera tras recargar sin otra intención ni duplicado",
          "t": 38
        }
      ],
      "saltados": [],
      "duracion": 48.3,
      "inicio": 0,
      "video": "video/01-hu02-ubicaciones.mp4",
      "poster": "video/01-hu02-ubicaciones.jpg"
    },
    {
      "id": "HU-05",
      "spec": "frontend/e2e/stock.spec.ts",
      "titulo": "Consultar existencias y reservar",
      "rol": "Responsable de almacén y operario",
      "historia": "Como responsable, quiero consultar físico, reservado y disponible por ubicación y lote, y reservar o liberar existencias para un pedido de cliente.",
      "previstos": [
        "Consulta de existencias, alta de pedido, reserva y liberación (recorrido habitual)",
        "Reserva superior a lo disponible (error)",
        "Operario sin permiso de reservas (permisos)",
        "Ubicación en conteo abierto rechaza la reserva (estado bloqueante)"
      ],
      "escenarios": [
        {
          "titulo": "E2E-01: el responsable consulta, el operario crea el pedido y el responsable reserva y libera",
          "t": 52.3
        }
      ],
      "saltados": [],
      "duracion": 53.9,
      "inicio": 48.3,
      "video": "video/02-hu05-existencias-reservas.mp4",
      "poster": "video/02-hu05-existencias-reservas.jpg"
    },
    {
      "id": "HU-03",
      "spec": "frontend/e2e/shipping.spec.ts",
      "titulo": "Registrar salida de mercancía",
      "rol": "Operario de almacén",
      "historia": "Como operario, quiero registrar la salida de mercancía de un pedido de cliente consumiendo su reserva, para que las existencias reflejen lo servido.",
      "previstos": [
        "Salida que consume la reserva propia y deja el pedido servido (recorrido habitual)",
        "Cantidad superior a la posible (error)",
        "Sesión sin permiso de salidas (permisos)",
        "Pedido no abierto u origen en conteo abierto (estado bloqueante)"
      ],
      "escenarios": [
        {
          "titulo": "E2E-02: el operario sirve un pedido consumiendo la reserva propia y lo deja servido",
          "t": 106.2
        }
      ],
      "saltados": [],
      "duracion": 38,
      "inicio": 102.2,
      "video": "video/03-hu03-salidas.mp4",
      "poster": "video/03-hu03-salidas.jpg"
    },
    {
      "id": "HU-04",
      "spec": "frontend/e2e/hu04-cycle-counts.spec.ts",
      "titulo": "Realizar inventarios cíclicos",
      "rol": "Operario (cuenta) y responsable de almacén (autoriza)",
      "historia": "Como responsable, quiero contar una parte del almacén, ver las diferencias y autorizar su ajuste con motivo, para que el sistema refleje lo que hay físicamente.",
      "previstos": [
        "Conteo con una diferencia, revisión con motivo y ajuste confirmado (recorrido habitual)",
        "Confirmar por debajo del reservado (error)",
        "Operario sin permiso de ajuste (permisos)",
        "Ubicación congelada rechaza salidas, reservas y cambios (estado bloqueante)"
      ],
      "escenarios": [
        {
          "titulo": "E2E-03: el operario cuenta una diferencia y el responsable confirma el ajuste",
          "t": 144.2
        }
      ],
      "saltados": [],
      "duracion": 29.2,
      "inicio": 140.2,
      "video": "video/04-hu04-inventarios-ciclicos.mp4",
      "poster": "video/04-hu04-inventarios-ciclicos.jpg"
    }
  ]
};
