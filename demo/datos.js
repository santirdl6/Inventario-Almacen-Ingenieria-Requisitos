// Generado por demo/montar.sh: no editar a mano.
window.DEMO = {
  "completa": "video/demo-completa.mp4",
  "duracion": 507.3,
  "historias": [
    {
      "id": "HU-02",
      "spec": "frontend/e2e/locations.spec.ts + frontend/e2e/demo/demo-hu02.spec.ts",
      "titulo": "Gestionar ubicaciones",
      "rol": "Operario de almacén (y cuenta sin gestión de ubicaciones)",
      "historia": "Como operario, quiero dar de alta, buscar, corregir y retirar ubicaciones para saber dónde está físicamente cada artículo.",
      "previstos": [
        "Alta, búsqueda, revisión y retirada de ubicaciones (recorrido habitual)",
        "Código duplicado o con caracteres no válidos (error)",
        "Cuenta sin Locations.Manage: ni alta ni retirada, también por API (permisos)",
        "Última zona de recepción activa: no se puede retirar (estado bloqueante)",
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
          "t": 38.2
        },
        {
          "titulo": "Código con caracteres no válidos: error de validación sin perder lo escrito",
          "t": 48.4
        },
        {
          "titulo": "No se puede retirar la última zona de recepción activa",
          "t": 91.4
        },
        {
          "titulo": "Permiso: sin Locations.Manage no se crea ni se retira una ubicación",
          "t": 104.1
        }
      ],
      "saltados": [],
      "duracion": 118.8,
      "inicio": 0,
      "video": "video/01-hu02-ubicaciones.mp4",
      "poster": "video/01-hu02-ubicaciones.jpg"
    },
    {
      "id": "HU-05",
      "spec": "frontend/e2e/stock.spec.ts + frontend/e2e/demo/demo-hu05.spec.ts",
      "titulo": "Consultar existencias y reservar",
      "rol": "Responsable de almacén y operario",
      "historia": "Como responsable, quiero consultar físico, reservado y disponible por ubicación y lote, y reservar o liberar existencias para un pedido de cliente.",
      "previstos": [
        "Consulta de existencias, alta de pedido, reserva y liberación (recorrido habitual)",
        "Consulta con y sin filtros: físico 0 oculto, artículo inactivo marcado",
        "Reserva superior a lo disponible (error)",
        "Liberación parcial con motivo obligatorio",
        "Operario sin Reservations.Manage (permisos)",
        "Ubicación en conteo abierto rechaza la reserva (estado bloqueante, en la demo de HU-04)"
      ],
      "escenarios": [
        {
          "titulo": "E2E-01: el responsable consulta, el operario crea el pedido y el responsable reserva y libera",
          "t": 122.8
        },
        {
          "titulo": "Consulta con y sin filtros: ubicaciones con físico 0 ocultas y artículo inactivo marcado",
          "t": 171.9
        },
        {
          "titulo": "Reservar más de lo disponible: el servidor lo rechaza y no reserva nada",
          "t": 195
        },
        {
          "titulo": "Liberar parte de una reserva con motivo obligatorio",
          "t": 210.7
        },
        {
          "titulo": "Permiso: el operario ve el pedido pero no puede reservar (Reservations.Manage)",
          "t": 230.7
        }
      ],
      "saltados": [],
      "duracion": 120.2,
      "inicio": 118.8,
      "video": "video/02-hu05-existencias-reservas.mp4",
      "poster": "video/02-hu05-existencias-reservas.jpg"
    },
    {
      "id": "HU-03",
      "spec": "frontend/e2e/shipping.spec.ts + frontend/e2e/demo/demo-hu03.spec.ts",
      "titulo": "Registrar salida de mercancía",
      "rol": "Operario de almacén",
      "historia": "Como operario, quiero registrar la salida de mercancía de un pedido de cliente consumiendo su reserva, para que las existencias reflejen lo servido.",
      "previstos": [
        "Salida que consume la reserva propia y deja el pedido servido (recorrido habitual)",
        "Stock insuficiente: aviso con detalle y rechazo (error)",
        "Salida que consume toda la reserva: reservado a 0",
        "Origen en conteo abierto: confirmar se rechaza (estado bloqueante)",
        "Cuenta sin Shipping.Record (permisos)"
      ],
      "escenarios": [
        {
          "titulo": "E2E-02: el operario sirve un pedido consumiendo la reserva propia y lo deja servido",
          "t": 243
        },
        {
          "titulo": "Salida con stock insuficiente: la revisión avisa con el detalle y el servidor la rechaza",
          "t": 275.5
        },
        {
          "titulo": "Salida que consume la reserva del pedido: reservado a 0 y pedido servido",
          "t": 294.4
        },
        {
          "titulo": "Salida cuyo origen entra en un conteo abierto: confirmar se rechaza (CountScopeFrozen)",
          "t": 316.4
        },
        {
          "titulo": "Permiso Shipping.Record: una cuenta sin él no prepara salidas",
          "t": 334.8
        }
      ],
      "saltados": [],
      "duracion": 108,
      "inicio": 239,
      "video": "video/03-hu03-salidas.mp4",
      "poster": "video/03-hu03-salidas.jpg"
    },
    {
      "id": "HU-04",
      "spec": "frontend/e2e/hu04-cycle-counts.spec.ts + frontend/e2e/demo/demo-hu04.spec.ts",
      "titulo": "Realizar inventarios cíclicos",
      "rol": "Operario (cuenta) y responsable de almacén (autoriza)",
      "historia": "Como responsable, quiero contar una parte del almacén, ver las diferencias y autorizar su ajuste con motivo, para que el sistema refleje lo que hay físicamente.",
      "previstos": [
        "Conteo con una diferencia, revisión con motivo y ajuste confirmado (recorrido habitual)",
        "Ubicación congelada rechaza reservas y cambios con CountScopeFrozen (estado bloqueante)",
        "Diferencias en dos ubicaciones: motivos, confirmación y ajuste del físico",
        "Aviso P-07: salida de 6 con 4 reales, el pedido no se corrige",
        "Operario sin Inventory.Adjust no confirma (permisos)"
      ],
      "escenarios": [
        {
          "titulo": "E2E-03: el operario cuenta una diferencia y el responsable confirma el ajuste",
          "t": 351
        },
        {
          "titulo": "Una ubicación en conteo rechaza reservas y cambios (CountScopeFrozen)",
          "t": 376.2
        },
        {
          "titulo": "Conteo con diferencias en dos ubicaciones: revisión con motivos y ajuste del físico",
          "t": 420.9
        },
        {
          "titulo": "Aviso P-07: una diferencia vinculada a una salida de 6 (salieron 4) no corrige el pedido",
          "t": 462.1
        },
        {
          "titulo": "Permiso: el operario no puede confirmar el ajuste (solo Inventory.Adjust)",
          "t": 487.9
        }
      ],
      "saltados": [],
      "duracion": 160.4,
      "inicio": 347,
      "video": "video/04-hu04-inventarios-ciclicos.mp4",
      "poster": "video/04-hu04-inventarios-ciclicos.jpg"
    }
  ]
};
